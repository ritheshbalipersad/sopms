using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

public class SOPController : Controller
{
    
    public IActionResult Management()
    {
        if (!User.Identity.IsAuthenticated)
        {
            return RedirectToAction("Login", "Account");
        }

        // Redirect Admins to the master dashboard
        if (User.IsInRole("Admin"))
        {
            return RedirectToAction("MasterData");
        }

        // Non-admins can be redirected or shown an access denied page
        return RedirectToAction("AccessDenied", "Account");
    }

    [Authorize(Roles = "Admin")]
    public IActionResult Upload()
    {
        return View();
    }

    [Authorize(Roles = "Admin")]
    public IActionResult MasterData()
    {
        return View();
    }
}
