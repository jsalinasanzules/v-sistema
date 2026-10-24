using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turnos.Api.Services;
namespace Turnos.Api.Controllers;
[ApiController,Authorize,Route("api/menu")]
public sealed class MenuController(MenuService menu):ControllerBase {
    [HttpGet] public async Task<IActionResult> Get() => Ok(await menu.ForRoleAsync(RequestUser.Role(User)));
}