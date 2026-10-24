using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turnos.Api.Services;
namespace Turnos.Api.Controllers;
[ApiController,Authorize(Roles="ADMINISTRADOR,GESTOR"),Route("api/users")]
public sealed class UsersController(UserService users):ControllerBase {
    [HttpGet] public async Task<IActionResult> List() => Ok(await users.ListAsync());
    [HttpPost] public async Task<IActionResult> Create(CreateUserRequest request) {
        try { var id=await users.CreateAsync(request,RequestUser.Id(User),RequestUser.Role(User)); return Created($"/api/users/{id}",new{id,status=RequestUser.Role(User)=="ADMINISTRADOR"?"APPROVED":"PENDING"}); }
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(UnauthorizedAccessException ex){return Forbid(ex.Message);}
        catch(Npgsql.PostgresException ex) when(ex.SqlState=="23505"){return Conflict(new{message="Usuario, identificación o correo ya registrado."});}
    }
    [Authorize(Roles="ADMINISTRADOR"),HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id) { try{await users.ApproveAsync(id,RequestUser.Id(User),true);return NoContent();}catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});} }
    [Authorize(Roles="ADMINISTRADOR"),HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id) { try{await users.ApproveAsync(id,RequestUser.Id(User),false);return NoContent();}catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});} }
    [Authorize(Roles="ADMINISTRADOR"),HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) { try{await users.SoftDeleteAsync(id,RequestUser.Id(User));return NoContent();}catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});} }
}