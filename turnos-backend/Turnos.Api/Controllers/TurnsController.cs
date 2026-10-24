using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turnos.Api.Services;
namespace Turnos.Api.Controllers;
[ApiController,Authorize,Route("api/turns")]
public sealed class TurnsController(TurnService turns):ControllerBase {
    [HttpGet("types")] public async Task<IActionResult> Types()=>Ok(await turns.TypesAsync());
    [HttpGet] public async Task<IActionResult> List()=>Ok(await turns.ListAsync());
    public sealed record CreateTurnRequest(short AttentionTypeId,Guid? ClientId);
    [HttpPost] public async Task<IActionResult> Create(CreateTurnRequest x) {
        try{return Ok(await turns.CreateAsync(x.AttentionTypeId,RequestUser.Id(User),x.ClientId));}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(Npgsql.PostgresException ex){return Conflict(new{message=ex.MessageText});}
    }
    public sealed record UpdateStatusRequest(string Status);
    [HttpPatch("{id:guid}/status")] public async Task<IActionResult> Update(Guid id,UpdateStatusRequest x) {
        try{await turns.UpdateStatusAsync(id,x.Status,RequestUser.Id(User));return NoContent();}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});}
    }
}