using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MetaSearch.Services;

namespace MetaSearch.Pages;

/// <summary>관리자 페이지 — config AdminUsers 또는 DB isadmin=1 인 계정만 접근 가능.</summary>
public class AdminModel : PageModel
{
    private readonly DbService _db;
    private readonly IConfiguration _config;

    public AdminModel(DbService db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>관리자가 아니면 메인으로 리다이렉트</summary>
    public IActionResult OnGet()
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/metasearch");

        // 1. config 파일에 등록된 관리자 확인
        var cfgAdmins = _config.GetSection("AdminUsers").Get<string[]>() ?? new[] { "test" };
        if (cfgAdmins.Any(a => string.Equals(a.Trim(), uid, StringComparison.OrdinalIgnoreCase)))
            return Page();

        // 2. DB isadmin 컬럼 확인
        try
        {
            using var conn = _db.OpenConnection();
            var val = Dapper.SqlMapper.ExecuteScalar<int?>(conn,
                "SELECT isadmin FROM members WHERE userid = @uid", new { uid });
            if (val == 1) return Page();
        }
        catch { }

        return Redirect("/metasearch");
    }
}
