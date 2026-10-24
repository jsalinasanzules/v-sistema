using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turnos.Api.Services;
namespace Turnos.Api.Controllers;
[ApiController,Authorize,Route("api")]
public sealed class BusinessController(BusinessService business):ControllerBase {
    [HttpGet("services")] public async Task<IActionResult> Services()=>Ok(await business.ServicesAsync());
    [Authorize(Roles="ADMINISTRADOR,GESTOR"),HttpPost("services")] public async Task<IActionResult> CreateService(ServiceRequest x) {
        try{return Created("/api/services",new{id=await business.CreateServiceAsync(x,RequestUser.Id(User))});}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
    }
    [HttpGet("contracts")] public async Task<IActionResult> Contracts()=>Ok(await business.ContractsAsync());
    [HttpGet("payments")] public async Task<IActionResult> Payments()=>Ok(await business.PaymentsAsync());
    [Authorize(Roles="ADMINISTRADOR,GESTOR"),HttpPost("contracts")] public async Task<IActionResult> CreateContract(ContractRequest x) {
        try{return Created("/api/contracts",await business.CreateContractAsync(x,RequestUser.Id(User)));}
        catch(Npgsql.PostgresException ex){return BadRequest(new{message=ex.MessageText});}
    }
    [Authorize(Roles="ADMINISTRADOR,GESTOR"),HttpPost("contracts/{id:guid}/replace")] public async Task<IActionResult> Replace(Guid id,ContractRequest x) {
        try{await business.ReplaceContractAsync(id,x,RequestUser.Id(User));return NoContent();}
        catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});}
        catch(Npgsql.PostgresException ex){return BadRequest(new{message=ex.MessageText});}
    }
    [Authorize(Roles="ADMINISTRADOR,GESTOR"),HttpPost("contracts/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id) {
        try{await business.CancelContractAsync(id,RequestUser.Id(User));return NoContent();}
        catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});}
    }
    [HttpPost("payments")] public async Task<IActionResult> Payment(PaymentRequest x) {
        try{return Created("/api/payments",new{id=await business.CreatePaymentAsync(x,RequestUser.Id(User))});}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(Npgsql.PostgresException ex){return BadRequest(new{message=ex.MessageText});}
    }
}