using System.Security.Cryptography;
using Dapper;

namespace MetaSearch.Services;

/// <summary>
/// 개인 클라우드 드라이브 서비스.
/// 인증(PBKDF2 해싱), 파일·폴더 CRUD, 즐겨찾기, 휴지통, 검색, 복사 등.
/// 물리 저장소: /mnt/ssd/drive/{userId}/
/// </summary>
public class DriveService
{
    private readonly DbService _db;

    // SSD 마운트 경로 (라즈베리파이)
    private const string STORAGE_ROOT = "/mnt/ssd/drive";

    // PBKDF2 해싱 설정 — OWASP 권장 수준
    private const int SALT_SIZE = 16;
    private const int HASH_SIZE = 32;
    private const int ITERATIONS = 100_000;

    public DriveService(DbService db) => _db = db;

    // ═══════════════════════════════════════════════════════════════
    // 입력 검증 — 보안 유틸리티
    // ═══════════════════════════════════════════════════════════════

    /// <summary>파일명 정리 — 경로 탐색 방지, 특수문자·제어문자 제거 (XSS/인젝션 차단)</summary>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unnamed";

        // 경로 구분자 제거 (디렉토리 탈출 방지)
        name = name.Replace("/", "").Replace("\\", "");
        // 상대 경로 토큰 제거
        name = name.Replace("..", "");
        // 제어 문자 제거 (null byte 등)
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        // OS 예약 문자 제거
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "");
        // 선행 마침표 제거 (Unix 숨김 파일 방지)
        name = name.Trim().TrimStart('.');
        if (string.IsNullOrWhiteSpace(name)) return "unnamed";
        // 길이 제한 (파일시스템 호환 — ext4 max 255)
        if (name.Length > 200) name = name[..200];
        return name;
    }

    /// <summary>ILIKE 패턴 이스케이프 — %, _, \ 와일드카드 문자 무력화</summary>
    public static string EscapeLikePattern(string input)
    {
        return input
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    /// <summary>만료된 드라이브 세션 정리 (주기적 호출용)</summary>
    public void CleanExpiredSessions()
    {
        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM drive_sessions WHERE expires_at < NOW()");
    }

    // ═══════════════════════════════════════════════════════════════
    // 스키마 마이그레이션 — 앱 시작 시 호출
    // ═══════════════════════════════════════════════════════════════

    /// <summary>새 컬럼 추가 (즐겨찾기, 휴지통)</summary>
    public void EnsureSchema()
    {
        using var conn = _db.OpenConnection();
        // 즐겨찾기 컬럼
        conn.Execute("ALTER TABLE drive_files ADD COLUMN IF NOT EXISTS is_starred BOOLEAN DEFAULT FALSE");
        // 휴지통 관련 컬럼
        conn.Execute("ALTER TABLE drive_files ADD COLUMN IF NOT EXISTS is_trashed BOOLEAN DEFAULT FALSE");
        conn.Execute("ALTER TABLE drive_files ADD COLUMN IF NOT EXISTS trashed_at TIMESTAMP");
    }

    // ═══════════════════════════════════════════════════════════════
    // 인증 관련
    // ═══════════════════════════════════════════════════════════════

    /// <summary>비밀번호를 PBKDF2-SHA256으로 해싱</summary>
    public string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, ITERATIONS, HashAlgorithmName.SHA256, HASH_SIZE);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>비밀번호 검증 — 타이밍 공격 방지용 고정시간 비교</summary>
    public bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expectedHash = Convert.FromBase64String(parts[1]);
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, ITERATIONS, HashAlgorithmName.SHA256, HASH_SIZE);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    /// <summary>앱 시작 시 기본 계정 자동 생성 (없으면)</summary>
    public void EnsureDefaultUser(string username, string password)
    {
        using var conn = _db.OpenConnection();
        var exists = conn.QueryFirstOrDefault<int?>(
            "SELECT id FROM drive_users WHERE username = @username", new { username });
        if (exists != null) return;

        string hash = HashPassword(password);
        conn.Execute(
            "INSERT INTO drive_users (username, password_hash) VALUES (@username, @hash)",
            new { username, hash });
    }

    /// <summary>로그인 — 성공 시 세션 토큰 반환, 실패 시 null</summary>
    public string? Login(string username, string password)
    {
        using var conn = _db.OpenConnection();
        var user = conn.QueryFirstOrDefault<(int Id, string Hash)>(
            "SELECT id, password_hash AS Hash FROM drive_users WHERE username = @username",
            new { username });

        if (user.Id == 0 || !VerifyPassword(password, user.Hash)) return null;

        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLower();

        conn.Execute("DELETE FROM drive_sessions WHERE expires_at < NOW()");
        conn.Execute(
            @"INSERT INTO drive_sessions (token, user_id, expires_at)
              VALUES (@token, @userId, NOW() + INTERVAL '7 days')",
            new { token, userId = user.Id });

        return token;
    }

    /// <summary>토큰으로 사용자 인증 — 유효하면 userId 반환 (형식 검증 포함)</summary>
    public int? ValidateToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        // 토큰 형식 검증 (64자 hex, 대소문자 무관) — 잘못된 형식은 DB 조회 없이 거부
        if (token.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9a-fA-F]{64}$"))
            return null;
        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<int?>(
            "SELECT user_id FROM drive_sessions WHERE token = @token AND expires_at > NOW()",
            new { token });
    }

    /// <summary>로그아웃 — 세션 토큰 삭제</summary>
    public void Logout(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM drive_sessions WHERE token = @token", new { token });
    }

    /// <summary>사용자 이름 조회</summary>
    public string? GetUsername(int userId)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<string>(
            "SELECT username FROM drive_users WHERE id = @userId", new { userId });
    }

    // ═══════════════════════════════════════════════════════════════
    // 파일·폴더 CRUD
    // ═══════════════════════════════════════════════════════════════

    // Dapper용 SELECT — snake_case → PascalCase 매핑
    private const string FILE_COLS =
        "id AS Id, file_name AS FileName, file_size AS FileSize, is_folder AS IsFolder, " +
        "parent_id AS ParentId, is_starred AS IsStarred, is_trashed AS IsTrashed, " +
        "trashed_at AS TrashedAt, created_at AS CreatedAt, updated_at AS UpdatedAt";

    /// <summary>해당 폴더의 하위 항목 목록 조회 (휴지통 제외, 폴더 우선, 이름순)</summary>
    public IEnumerable<DriveItem> ListFiles(int userId, int? parentId)
    {
        using var conn = _db.OpenConnection();
        string sql = parentId == null
            ? $"SELECT {FILE_COLS} FROM drive_files WHERE user_id = @userId AND parent_id IS NULL AND is_trashed = FALSE ORDER BY is_folder DESC, file_name"
            : $"SELECT {FILE_COLS} FROM drive_files WHERE user_id = @userId AND parent_id = @parentId AND is_trashed = FALSE ORDER BY is_folder DESC, file_name";

        return conn.Query<DriveItem>(sql, new { userId, parentId });
    }

    /// <summary>폴더 생성 — 새 폴더 ID 반환</summary>
    public int CreateFolder(int userId, string name, int? parentId)
    {
        using var conn = _db.OpenConnection();
        return conn.QuerySingle<int>(
            @"INSERT INTO drive_files (user_id, file_name, is_folder, parent_id)
              VALUES (@userId, @name, TRUE, @parentId) RETURNING id",
            new { userId, name, parentId });
    }

    /// <summary>파일 메타데이터 DB에 등록 — 새 파일 ID 반환</summary>
    public int RegisterFile(int userId, string fileName, long fileSize, int? parentId)
    {
        using var conn = _db.OpenConnection();
        return conn.QuerySingle<int>(
            @"INSERT INTO drive_files (user_id, file_name, file_size, is_folder, parent_id)
              VALUES (@userId, @fileName, @fileSize, FALSE, @parentId) RETURNING id",
            new { userId, fileName, fileSize, parentId });
    }

    /// <summary>같은 폴더에 동일 이름 파일이 있는지 확인 — 있으면 ID 반환</summary>
    public int? FindDuplicate(int userId, string fileName, int? parentId)
    {
        using var conn = _db.OpenConnection();
        string sql = parentId == null
            ? "SELECT id FROM drive_files WHERE user_id = @userId AND file_name = @fileName AND parent_id IS NULL AND is_trashed = FALSE AND is_folder = FALSE"
            : "SELECT id FROM drive_files WHERE user_id = @userId AND file_name = @fileName AND parent_id = @parentId AND is_trashed = FALSE AND is_folder = FALSE";
        return conn.QueryFirstOrDefault<int?>(sql, new { userId, fileName, parentId });
    }

    /// <summary>기존 파일 크기/날짜 업데이트 (덮어쓰기용)</summary>
    public void UpdateFileSize(int userId, int fileId, long newSize)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            "UPDATE drive_files SET file_size = @newSize, updated_at = NOW() WHERE id = @fileId AND user_id = @userId",
            new { newSize, fileId, userId });
    }

    /// <summary>중복 방지 이름 생성 — "파일 (1).txt", "파일 (2).txt" 등</summary>
    public string GetUniqueName(int userId, string fileName, int? parentId)
    {
        string ext = Path.GetExtension(fileName);
        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
        string candidate = fileName;
        int n = 1;
        while (FindDuplicate(userId, candidate, parentId) != null)
        {
            candidate = $"{nameOnly} ({n}){ext}";
            n++;
        }
        return candidate;
    }

    /// <summary>파일 물리 저장 경로 반환</summary>
    public string GetPhysicalPath(int userId, int fileId)
    {
        return Path.Combine(STORAGE_ROOT, userId.ToString(), fileId.ToString());
    }

    /// <summary>사용자 저장소 루트 디렉토리 확보</summary>
    public string EnsureUserDir(int userId)
    {
        return Path.Combine(STORAGE_ROOT, userId.ToString());
    }

    /// <summary>파일 정보 조회</summary>
    public DriveItem? GetFile(int userId, int fileId)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<DriveItem>(
            $"SELECT {FILE_COLS} FROM drive_files WHERE id = @fileId AND user_id = @userId",
            new { fileId, userId });
    }

    /// <summary>파일/폴더 영구 삭제 (하위 항목 포함)</summary>
    public bool Delete(int userId, int fileId)
    {
        var item = GetFile(userId, fileId);
        if (item == null) return false;

        using var conn = _db.OpenConnection();

        if (item.IsFolder)
        {
            conn.Query<int>(
                @"WITH RECURSIVE sub AS (
                    SELECT id, is_folder FROM drive_files WHERE parent_id = @fileId AND user_id = @userId
                    UNION ALL
                    SELECT f.id, f.is_folder FROM drive_files f JOIN sub s ON f.parent_id = s.id
                  )
                  SELECT id FROM sub WHERE is_folder = FALSE",
                new { fileId, userId });
        }

        conn.Execute("DELETE FROM drive_files WHERE id = @fileId AND user_id = @userId",
            new { fileId, userId });
        return true;
    }

    /// <summary>폴더 내 모든 파일 ID 재귀 조회 (물리 파일 삭제용)</summary>
    public IEnumerable<int> GetAllFileIdsInFolder(int userId, int folderId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<int>(
            @"WITH RECURSIVE sub AS (
                SELECT id, is_folder FROM drive_files WHERE parent_id = @folderId AND user_id = @userId
                UNION ALL
                SELECT f.id, f.is_folder FROM drive_files f JOIN sub s ON f.parent_id = s.id
              )
              SELECT id FROM sub WHERE is_folder = FALSE",
            new { folderId, userId });
    }

    /// <summary>파일/폴더 이름 변경</summary>
    public bool Rename(int userId, int fileId, string newName)
    {
        using var conn = _db.OpenConnection();
        int affected = conn.Execute(
            "UPDATE drive_files SET file_name = @newName, updated_at = NOW() WHERE id = @fileId AND user_id = @userId",
            new { newName, fileId, userId });
        return affected > 0;
    }

    /// <summary>파일/폴더를 다른 폴더로 이동</summary>
    public bool Move(int userId, int fileId, int? newParentId)
    {
        if (newParentId == fileId) return false;
        using var conn = _db.OpenConnection();
        int affected = conn.Execute(
            "UPDATE drive_files SET parent_id = @newParentId, updated_at = NOW() WHERE id = @fileId AND user_id = @userId",
            new { newParentId, fileId, userId });
        return affected > 0;
    }

    /// <summary>현재 폴더 경로(브레드크럼) — 루트부터 현재까지</summary>
    public List<(int Id, string Name)> GetBreadcrumb(int userId, int? folderId)
    {
        if (folderId == null) return new();
        var crumbs = new List<(int, string)>();
        using var conn = _db.OpenConnection();
        int? current = folderId;

        while (current != null)
        {
            var folder = conn.QueryFirstOrDefault<(int Id, string Name, int? ParentId)>(
                "SELECT id, file_name AS Name, parent_id AS ParentId FROM drive_files WHERE id = @id AND user_id = @userId",
                new { id = current, userId });
            if (folder.Id == 0) break;
            crumbs.Insert(0, (folder.Id, folder.Name));
            current = folder.ParentId;
        }
        return crumbs;
    }

    /// <summary>사용자 전체 사용량(바이트) 조회</summary>
    public long GetUsage(int userId)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<long>(
            "SELECT COALESCE(SUM(file_size), 0) FROM drive_files WHERE user_id = @userId AND is_folder = FALSE AND is_trashed = FALSE",
            new { userId });
    }

    // ═══════════════════════════════════════════════════════════════
    // 즐겨찾기
    // ═══════════════════════════════════════════════════════════════

    /// <summary>즐겨찾기 토글 — 변경 후 상태 반환</summary>
    public bool ToggleStar(int userId, int fileId)
    {
        using var conn = _db.OpenConnection();
        return conn.QuerySingle<bool>(
            @"UPDATE drive_files SET is_starred = NOT is_starred, updated_at = NOW()
              WHERE id = @fileId AND user_id = @userId
              RETURNING is_starred",
            new { fileId, userId });
    }

    /// <summary>즐겨찾기 목록 조회</summary>
    public IEnumerable<DriveItem> ListStarred(int userId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<DriveItem>(
            $"SELECT {FILE_COLS} FROM drive_files WHERE user_id = @userId AND is_starred = TRUE AND is_trashed = FALSE ORDER BY is_folder DESC, file_name",
            new { userId });
    }

    // ═══════════════════════════════════════════════════════════════
    // 휴지통
    // ═══════════════════════════════════════════════════════════════

    /// <summary>파일/폴더를 휴지통으로 이동</summary>
    public bool Trash(int userId, int fileId)
    {
        using var conn = _db.OpenConnection();
        int affected = conn.Execute(
            "UPDATE drive_files SET is_trashed = TRUE, trashed_at = NOW() WHERE id = @fileId AND user_id = @userId",
            new { fileId, userId });
        return affected > 0;
    }

    /// <summary>휴지통에서 복원</summary>
    public bool Restore(int userId, int fileId)
    {
        using var conn = _db.OpenConnection();
        int affected = conn.Execute(
            "UPDATE drive_files SET is_trashed = FALSE, trashed_at = NULL WHERE id = @fileId AND user_id = @userId",
            new { fileId, userId });
        return affected > 0;
    }

    /// <summary>휴지통 목록 조회</summary>
    public IEnumerable<DriveItem> ListTrash(int userId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<DriveItem>(
            $"SELECT {FILE_COLS} FROM drive_files WHERE user_id = @userId AND is_trashed = TRUE ORDER BY trashed_at DESC",
            new { userId });
    }

    /// <summary>휴지통에서 영구 삭제할 모든 파일 ID 조회</summary>
    public IEnumerable<int> GetTrashFileIds(int userId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<int>(
            "SELECT id FROM drive_files WHERE user_id = @userId AND is_trashed = TRUE AND is_folder = FALSE",
            new { userId });
    }

    /// <summary>휴지통 비우기 — 모든 휴지통 항목 영구 삭제</summary>
    public void EmptyTrash(int userId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM drive_files WHERE user_id = @userId AND is_trashed = TRUE",
            new { userId });
    }

    // ═══════════════════════════════════════════════════════════════
    // 검색
    // ═══════════════════════════════════════════════════════════════

    /// <summary>파일명으로 검색 (대소문자 무시, 휴지통 제외, 와일드카드 이스케이프)</summary>
    public IEnumerable<DriveItem> Search(int userId, string query)
    {
        using var conn = _db.OpenConnection();
        // ILIKE 특수문자(%, _, \)를 이스케이프하여 리터럴 검색
        string escaped = EscapeLikePattern(query);
        return conn.Query<DriveItem>(
            $"SELECT {FILE_COLS} FROM drive_files WHERE user_id = @userId AND is_trashed = FALSE AND file_name ILIKE @pattern ESCAPE '\\' ORDER BY is_folder DESC, file_name LIMIT 100",
            new { userId, pattern = $"%{escaped}%" });
    }

    // ═══════════════════════════════════════════════════════════════
    // 최근 파일
    // ═══════════════════════════════════════════════════════════════

    /// <summary>최근 수정된 파일 목록 (폴더 제외)</summary>
    public IEnumerable<DriveItem> ListRecent(int userId, int limit = 30)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<DriveItem>(
            $"SELECT {FILE_COLS} FROM drive_files WHERE user_id = @userId AND is_trashed = FALSE AND is_folder = FALSE ORDER BY updated_at DESC LIMIT @limit",
            new { userId, limit });
    }

    // ═══════════════════════════════════════════════════════════════
    // 파일 복사
    // ═══════════════════════════════════════════════════════════════

    /// <summary>파일 복제 — DB 레코드 생성, 물리 파일은 API에서 복사. 새 ID 반환</summary>
    public int? CopyFile(int userId, int fileId)
    {
        var item = GetFile(userId, fileId);
        if (item == null || item.IsFolder) return null;

        using var conn = _db.OpenConnection();
        // "파일명 (복사)" 형태로 이름 생성
        string ext = Path.GetExtension(item.FileName);
        string nameOnly = Path.GetFileNameWithoutExtension(item.FileName);
        string newName = $"{nameOnly} (복사){ext}";

        return conn.QuerySingle<int>(
            @"INSERT INTO drive_files (user_id, file_name, file_size, is_folder, parent_id)
              VALUES (@userId, @newName, @fileSize, FALSE, @parentId) RETURNING id",
            new { userId, newName, fileSize = item.FileSize, parentId = item.ParentId });
    }
}

/// <summary>드라이브 파일/폴더 DTO</summary>
public class DriveItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public bool IsFolder { get; set; }
    public int? ParentId { get; set; }
    public bool IsStarred { get; set; }
    public bool IsTrashed { get; set; }
    public DateTime? TrashedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
