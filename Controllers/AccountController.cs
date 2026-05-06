using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;
using System.Security.Claims;

namespace CarbonTrack.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser>  _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole>     _roleManager;
        private readonly CarbonTrackContext            _context;
        private readonly ILogger<AccountController>    _logger;

        public AccountController(
            UserManager<ApplicationUser>  userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole>     roleManager,
            CarbonTrackContext            context,
            ILogger<AccountController>    logger)
        {
            _userManager  = userManager;
            _signInManager = signInManager;
            _roleManager   = roleManager;
            _context       = context;
            _logger        = logger;
        }

        // ── Login ──────────────────────────────────────────────────────────────

        [AllowAnonymous]
        public IActionResult Login(string? returnUrl)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _signInManager.PasswordSignInAsync(
                model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                _logger.LogInformation("User {Email} signed in", model.Email);
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError("", "Invalid email or password.");
            return View(model);
        }

        // ── Register (creates org + first admin) ───────────────────────────────

        [AllowAnonymous]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");
            return View();
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                // If no users exist yet, claim the seeded placeholder org.
                // Otherwise create a new org for this admin.
                Organisation org;
                if (!await _userManager.Users.AnyAsync())
                {
                    org = await _context.Organisations.OrderBy(o => o.Id).FirstOrDefaultAsync()
                          ?? new Organisation { Plan = "Free Trial", CreatedAt = DateTime.UtcNow };

                    org.Name         = model.OrgName.Trim();
                    org.ContactEmail = model.Email.Trim();
                    if (org.Id == 0) _context.Organisations.Add(org);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    org = new Organisation
                    {
                        Name         = model.OrgName.Trim(),
                        ContactEmail = model.Email.Trim(),
                        Plan         = "Free Trial",
                        CreatedAt    = DateTime.UtcNow,
                    };
                    _context.Organisations.Add(org);
                    await _context.SaveChangesAsync();
                }

                var user = new ApplicationUser
                {
                    UserName       = model.Email.Trim(),
                    Email          = model.Email.Trim(),
                    FullName       = model.FullName.Trim(),
                    OrganisationId = org.Id,
                };

                var result = await _userManager.CreateAsync(user, model.Password);
                if (!result.Succeeded)
                {
                    foreach (var e in result.Errors)
                        ModelState.AddModelError("", e.Description);
                    return View(model);
                }

                await _userManager.AddToRoleAsync(user, "Admin");
                await _signInManager.SignInAsync(user, isPersistent: false);
                _logger.LogInformation("New admin registered: {Email} / org {OrgId}", user.Email, org.Id);

                TempData["Success"] = "Welcome to CarbonTrack! Complete your profile to personalise the dashboard.";
                return RedirectToAction("Index", "Onboarding");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed for {Email}", model.Email);
                ModelState.AddModelError("", "Registration failed. Please try again.");
                return View(model);
            }
        }

        // ── Logout ─────────────────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        // ── Team management (Admin + Consultant) ───────────────────────────────

        [Authorize(Roles = "Admin,Consultant")]
        public async Task<IActionResult> Manage()
        {
            if (TempData["Success"] is string s) ViewBag.Success = s;
            if (TempData["Error"]   is string e) ViewBag.Error   = e;

            var currentUser = await _userManager.GetUserAsync(User);
            var orgId = currentUser?.OrganisationId ?? 1;

            var members = await _userManager.Users
                .Where(u => u.OrganisationId == orgId)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var membersWithRoles = new List<(ApplicationUser User, IList<string> Roles)>();
            foreach (var m in members)
                membersWithRoles.Add((m, await _userManager.GetRolesAsync(m)));

            var invites = await _context.Invites
                .Where(i => i.OrganisationId == orgId)
                .OrderByDescending(i => i.CreatedAt)
                .Take(20)
                .ToListAsync();

            var org = await _context.Organisations.FindAsync(orgId);

            ViewBag.MembersWithRoles = membersWithRoles;
            ViewBag.Invites          = invites;
            ViewBag.Org              = org;
            ViewBag.BaseUrl          = $"{Request.Scheme}://{Request.Host}";

            return View();
        }

        [Authorize(Roles = "Admin,Consultant"), HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateInvite(string email, string role)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var orgId = currentUser?.OrganisationId ?? 1;

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["Error"] = "Email address is required.";
                return RedirectToAction("Manage");
            }

            var validRoles = new[] { "Admin", "Employee" };
            if (!validRoles.Contains(role)) role = "Employee";

            // Expire any previous unused invite for this email+org
            var existing = await _context.Invites
                .Where(i => i.Email == email && i.OrganisationId == orgId && !i.Used)
                .ToListAsync();
            _context.Invites.RemoveRange(existing);

            var invite = new Invite
            {
                Email          = email.Trim().ToLowerInvariant(),
                Token          = Guid.NewGuid().ToString("N"),
                Role           = role,
                OrganisationId = orgId,
                CreatedAt      = DateTime.UtcNow,
                ExpiresAt      = DateTime.UtcNow.AddDays(7),
            };
            _context.Invites.Add(invite);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Invite created for {Email} (role: {Role}, org: {OrgId})", email, role, orgId);
            TempData["Success"] = $"Invite link generated for {email}. Copy the link from the table below.";
            return RedirectToAction("Manage");
        }

        // ── Accept invite (employee registration) ──────────────────────────────

        [AllowAnonymous]
        public async Task<IActionResult> AcceptInvite(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return RedirectToAction("Login");

            var invite = await _context.Invites
                .Include(i => i.Organisation)
                .FirstOrDefaultAsync(i => i.Token == token && !i.Used && i.ExpiresAt > DateTime.UtcNow);

            if (invite == null)
            {
                ViewBag.Error = "This invite link is invalid or has expired. Ask your admin to generate a new one.";
                return View("InviteExpired");
            }

            return View(new AcceptInviteViewModel
            {
                Token       = token,
                InviteEmail = invite.Email,
                OrgName     = invite.Organisation?.Name ?? "",
            });
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptInvite(AcceptInviteViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var invite = await _context.Invites
                .Include(i => i.Organisation)
                .FirstOrDefaultAsync(i => i.Token == model.Token && !i.Used && i.ExpiresAt > DateTime.UtcNow);

            if (invite == null)
            {
                ModelState.AddModelError("", "This invite link is invalid or has expired.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName       = invite.Email,
                Email          = invite.Email,
                FullName       = model.FullName.Trim(),
                OrganisationId = invite.OrganisationId,
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                    ModelState.AddModelError("", e.Description);
                return View(model);
            }

            await _userManager.AddToRoleAsync(user, invite.Role);

            invite.Used = true;
            await _context.SaveChangesAsync();

            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("User {Email} accepted invite, role {Role}, org {OrgId}",
                user.Email, invite.Role, invite.OrganisationId);

            TempData["Success"] = $"Welcome to CarbonTrack, {user.FullName}!";
            return RedirectToAction("Index", "Dashboard");
        }
    }
}
