using DocumentFormat.OpenXml.Bibliography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SOPMSApp.Models;
using SOPMSApp.ViewModels;
using System.Data;
using System.Security.Claims;

namespace SOPMSApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public AccountController(IConfiguration configuration, IWebHostEnvironment env)
        {
            _configuration = configuration;
            _env = env;
        }

        [HttpGet]
        public IActionResult Login()
        {
            // Clear session and sign out user on every visit to login
            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            //HttpContext.Session.Clear();
            return View(new LoginViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel Model)
        {
            if (!ModelState.IsValid)
            {
                return View(Model);
            }

            string userGuid = "";

            // Development-only test login (bypasses MCRegistrationSA); remove or disable in production
            const string TestUserGuid = "A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D";
            if (_env.IsDevelopment() && string.Equals(Model.logon, "test", StringComparison.OrdinalIgnoreCase) && Model.Password == "test")
            {
                userGuid = TestUserGuid;
            }
            else
            {
                string connStr = _configuration.GetConnectionString("LoginConnection");
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    string sql = "exec PSPT_WebAuthenticate @Username, @Password, @UserGuid"; //we are calling a  prodedure that is stored in SQL
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Username", Model.logon);
                        cmd.Parameters.AddWithValue("@Password", Model.Password);
                        cmd.Parameters.AddWithValue("@UserGuid", "c3a24f7b-8766-11d5-9e07-00104bca94f0");

                        await conn.OpenAsync();
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var resultCode = reader.GetValue(0).ToString();
                                if (resultCode == "-600" || resultCode == "-2300")
                                {
                                    TempData["ErrorMessage"] = "Wrong username or password. Please try again or go to Forgot Password.";
                                    return View(Model);
                                }
                                else
                                {
                                    userGuid = reader["user_guid"].ToString();
                                }
                            }
                            else
                            {
                                TempData["ErrorMessage"] = "Login failed. Please try again.";
                                return View(Model);
                            }
                        }
                    }
                }
            }

            LaborUserInfo laborInfo = await GetLaborUserInfoAsync(userGuid);
            if (laborInfo == null)
            {
                TempData["ErrorMessage"] = "User details not found in TTSAP database.";
                return View(Model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, Model.logon ),
                new Claim(ClaimTypes.Role, laborInfo.Role ?? "Admin"),
                new Claim("UserGuid", userGuid),
                new Claim("LaborID", laborInfo.LaborID ?? ""),
                new Claim("LaborName", laborInfo.LaborName ?? ""),
                new Claim("AccessGroupID", laborInfo.AccessGroupID ?? ""),
                new Claim("Email", laborInfo.Email ?? ""),
                new Claim("DepartmentID", laborInfo.DepartmentID?? "")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Optional: still store in session
            HttpContext.Session.SetString("LOGON", Model.logon);
            HttpContext.Session.SetString("USERGUID", userGuid);

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            // Clearing authentication cookies
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Clearing session data
            HttpContext.Session.Clear();

            // Clearing cached data
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Expires"] = "0";
            Response.Headers["Pragma"] = "no-cache";

            return RedirectToAction("Index", "Home");
        }


        private async Task<LaborUserInfo> GetLaborUserInfoAsync(string userGuid)
        {
            LaborUserInfo laborInfo = null;
            string connStr = _configuration.GetConnectionString("entTTSAPConnection");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                // string sql = "SELECT l.laborid, l.laborname, l.craftname, ISNULL(l.departmentname, 'No Department') AS DepartmentName, l.SupervisorID, l.SupervisorName, l.ShopID,l.ShopName,l.email, l.user_guid,\r\n    (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP10' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPAccess,\r\n    (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP20' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPUploadAccess,\r\n    (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP30' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPAprove,\r\n    (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP40' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPView,\r\n    (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP50' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPReview,\r\n    CASE\r\n        WHEN (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP20' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) = 1\r\n             AND (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP30' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) = 1 THEN 'Admin'\r\n        WHEN (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP20' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) = 1 THEN 'Manager'\r\n        ELSE 'User'\r\n    END AS Role\r\nFROM  labor l\r\nLEFT JOIN  department d ON d.DepartmentID = l.DepartmentID\r\nWHERE  l.user_guid = @UserGuid  AND l.labortype = 'EMP'  AND l.active = 1";
                string strsql = "SELECT  l.laborid, l.laborname, l.craftname,ISNULL(l.departmentname, 'No Department') AS DepartmentName," +
                                "l.SupervisorID,  l.SupervisorName,l.ShopID, l.ShopName, l.email, l.user_guid, " +
                                " (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP10' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPAccess, " +
                                " (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP20' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPUploadAccess, " +
                                " (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP30' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPAprove, " +
                                " (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP40' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPView, " +
                                " (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND actionid = 'SOP50' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) AS SOPReview, " +
                                " CASE " +
                                " WHEN (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND (accessgroupactions.AccessGroupPK IN ('19' , '22')) AND l.Access = '1' and accessgroupactions.AccessGroupPK = l.accessgrouppk) = 1 THEN 'Admin' " +
                                " WHEN (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND (accessgroupactions.AccessGroupPK IN ('18' , '23')) AND l.Access = '1' AND accessgroupactions.AccessGroupPK = l.accessgrouppk) = 1 THEN 'Manager' " +
                                " WHEN (SELECT TOP 1 enabled FROM accessgroupactions WHERE moduleid = 'ZS' AND (accessgroupactions.AccessGroupPK IN ('20' , '25')) AND l.Access = '1' AND accessgroupactions.AccessGroupPK = l.accessgrouppk and accessgroupactions.Enabled = 1) = 1 THEN 'Technician' " +
                                " ELSE 'User ' " +
                                " END AS Role " +
                                " FROM labor l " +
                                " LEFT JOIN department d ON d.DepartmentID = l.DepartmentID " +
                                " WHERE l.user_guid = @UserGuid  AND l.labortype = 'EMP' AND l.active = 1";



                using (SqlCommand cmd = new SqlCommand(strsql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserGuid", userGuid);
                    await conn.OpenAsync();

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            laborInfo = new LaborUserInfo
                            {
                                LaborID = reader["Laborid"]?.ToString(),
                                LaborName = reader["LaborName"]?.ToString(),
                                Role = reader["Role"]?.ToString(), // or "CraftID"
                                Email = reader["Email"]?.ToString(),
                                DepartmentID = reader["DepartmentName"]?.ToString()
                            };
                        }
                    }

                }
            }

            return laborInfo;
        }

    }
}
