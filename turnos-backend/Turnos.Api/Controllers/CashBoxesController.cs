using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turnos.Api.Services;
namespace Turnos.Api.Controllers;
[ApiController,Authorize(Roles="ADMINISTRADOR,GESTOR"),Route("api/cashboxes")]
public sealed class CashBoxesController(CashBoxService boxes):ControllerBase {
    [HttpGet] public async Task<IActionResult> List()=>Ok(await boxes.ListAsync());
    public sealed record CreateBoxRequest(string Code,string Name,string? Description);
    [HttpPost] public async Task<IActionResult> Create(CreateBoxRequest x) {
        try{return Created("/api/cashboxes",new{id=await boxes.CreateAsync(x.Code,x.Name,x.Description,RequestUser.Id(User))});}
        catch(Npgsql.PostgresException ex) when(ex.SqlState=="23505"){return Conflict(new{message="El código de caja ya existe."});}
    }
    public sealed record AssignRequest(Guid CashierUserId);
    [HttpPost("{id:guid}/assign")] public async Task<IActionResult> Assign(Guid id,AssignRequest x) {
        try{await boxes.AssignAsync(id,x.CashierUserId,RequestUser.Id(User));return NoContent();}
        catch(Npgsql.PostgresException ex){return BadRequest(new{message=ex.MessageText});}
    }
}