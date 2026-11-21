using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>
/// 포켓몬스터 아스테라 관리자 페이지 (/pokeadmin).
/// 회원별 게임 기록(진행·돈·파티·박스·가방)을 보고 수정한다. 사이트 관리자(members.isadmin = 1)만 접근.
/// 데이터 읽기/쓰기는 /api/astera/admin/* (서버에서 다시 관리자 확인).
/// </summary>
public class PokeAdminModel : PageModel
{
    private readonly Services.DbService _db;
    public PokeAdminModel(Services.DbService db) { _db = db; }

    public string AdminId { get; private set; } = "";
    /// <summary>게임 데이터 스크립트 캐시 무효화용 (Cloudflare가 오래된 파일을 주지 않게)</summary>
    public long Bust { get; private set; }

    public IActionResult OnGet()
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/signin?returnUrl=/pokeadmin");
        using var conn = _db.OpenConnection();
        bool admin = Dapper.SqlMapper.ExecuteScalar<int?>(conn, "SELECT isadmin FROM members WHERE userid = @uid", new { uid }) == 1;
        if (!admin) return StatusCode(403, "관리자만 볼 수 있는 페이지입니다.");
        AdminId = uid;
        Bust = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;   // 1분 단위
        Response.Headers.CacheControl = "no-store";
        return Page();
    }
}
