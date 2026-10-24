using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turnos.Api.Services;
namespace Turnos.Api.Controllers;
[ApiController,Authorize,Route("api/clients")]
public sealed class ClientsController(ClientService clients):ControllerBase {
    [HttpGet] public async Task<IActionResult> List()=>Ok(await clients.ListAsync());
    [HttpPost] public async Task<IActionResult> Create(ClientRequest x) {
        try{return Created("/api/clients",new{id=await clients.CreateAsync(x,RequestUser.Id(User))});}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(Npgsql.PostgresException ex) when(ex.SqlState=="23505"){return Conflict(new{message="La identificación ya está registrada."});}
    }
}