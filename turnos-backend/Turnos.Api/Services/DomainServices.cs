using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Turnos.Api.Services;

public sealed class UserService(IConfiguration config, DataProtectionService crypto, AuditService audit)
{
    public async Task<IEnumerable<object>> ListAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("""
          select u.id,u.username,u.first_name as "firstName",u.last_name as "lastName",r.code as role,
                 u.status,u.is_active as "isActive",u.is_blocked as "isBlocked",u.created_at as "createdAt"
          from app.users u join app.roles r on r.id=u.role_id where u.deleted_at is null order by u.created_at desc
          """);
    }
    public async Task<Guid> CreateAsync(CreateUserRequest x, Guid actor, string actorRole) {
        ValidateUsername(x.Username); ValidatePassword(x.Password);
        if (x.Identification.Length is <10 or >13 || !x.Identification.All(char.IsDigit)) throw new ArgumentException("La identificación debe tener entre 10 y 13 dígitos.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(x.Email, @"^[^@s]+@[^@s]+.[^@s]+$")) throw new ArgumentException("Correo inválido.");
        await using var db=await Db.Source(config).OpenConnectionAsync();
        var role = await db.QuerySingleOrDefaultAsync<short>("select id from app.roles where code=@role and is_active", new { role=x.Role });
        if(role==0) throw new ArgumentException("Rol no válido.");
        if(actorRole!="ADMINISTRADOR" && x.Role=="ADMINISTRADOR") throw new UnauthorizedAccessException("Solo el administrador puede crear administradores.");
        var id=Guid.NewGuid();
        await db.ExecuteAsync("""
          insert into app.users(id,role_id,username,password_hash,identification_cipher,identification_lookup,email_cipher,email_lookup,
            first_name,last_name,status,created_by)
          values(@id,@roleId,@username,@hash,@identCipher,@identLookup,@emailCipher,@emailLookup,@first,@last,@status,@actor)
          """,new { id, roleId=role, username=x.Username, hash=BCrypt.Net.BCrypt.HashPassword(x.Password,12),
            identCipher=crypto.Encrypt(x.Identification),identLookup=crypto.Lookup(x.Identification),
            emailCipher=crypto.Encrypt(x.Email),emailLookup=crypto.Lookup(x.Email),first=x.FirstName.Trim(),last=x.LastName.Trim(),
            status=actorRole=="ADMINISTRADOR"?"APPROVED":"PENDING",actor });
        await audit.WriteAsync(actor,"CREATE","users",id.ToString(),new { x.Username,x.Role });
        return id;
    }
    public async Task ApproveAsync(Guid id, Guid actor, bool approve) {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        var rows=await db.ExecuteAsync("""
          update app.users set status=@status,approved_by=@actor,approved_at=now(),updated_at=now()
          where id=@id and status='PENDING' and deleted_at is null
          """,new {id,actor,status=approve?"APPROVED":"REJECTED"});
        if(rows==0) throw new KeyNotFoundException("Usuario pendiente no encontrado.");
        await audit.WriteAsync(actor,approve?"APPROVE":"REJECT","users",id.ToString());
    }
    public async Task SoftDeleteAsync(Guid id, Guid actor) {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        var n=await db.ExecuteAsync("update app.users set deleted_at=now(),is_active=false,status='INACTIVE',updated_at=now() where id=@id and deleted_at is null",new{id});
        if(n==0) throw new KeyNotFoundException("Usuario no encontrado.");
        await audit.WriteAsync(actor,"DELETE","users",id.ToString());
    }
    private static void ValidateUsername(string s) {
        if(!System.Text.RegularExpressions.Regex.IsMatch(s,@"^(?=.*[A-Za-z])(?=.*[0-9])[A-Za-z0-9]{8,20}$"))
            throw new ArgumentException("El usuario debe tener 8-20 caracteres alfanuméricos, al menos una letra y un número, sin símbolos.");
    }
    private static void ValidatePassword(string s) {
        if(s.Length is <8 or >30 || !s.Any(char.IsUpper) || !s.Any(char.IsDigit))
            throw new ArgumentException("La contraseña debe tener 8-30 caracteres, al menos una mayúscula y un número.");
    }
}
public sealed record CreateUserRequest(string Username,string Password,string Identification,string Email,string FirstName,string LastName,string Role);

public sealed class TurnService(IConfiguration config, AuditService audit)
{
    public async Task<IEnumerable<object>> TypesAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("select id,code_prefix as prefix,name,description from app.attention_types where is_active order by name");
    }
    public async Task<IEnumerable<object>> ListAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("""
          select t.id,t.code,t.status,t.assigned_at as "assignedAt",t.called_at as "calledAt",t.started_at as "startedAt",
          t.completed_at as "completedAt",a.name as "attentionType",c.first_name as "clientFirstName",c.last_name as "clientLastName",
          t.cash_box_id as "cashBoxId",t.cashier_user_id as "cashierUserId"
          from app.turns t join app.attention_types a on a.id=t.attention_type_id
          left join app.clients c on c.id=t.client_id where t.deleted_at is null order by t.assigned_at desc limit 500
          """);
    }
    public async Task<object> CreateAsync(short typeId, Guid actor, Guid? clientId) {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        await using var tx=await db.BeginTransactionAsync();
        var code=await db.ExecuteScalarAsync<string>("select app.generate_turn_code(@typeId)",new{typeId},tx);
        var id=Guid.NewGuid();
        await db.ExecuteAsync("insert into app.turns(id,code,attention_type_id,client_id,assigned_by,status) values(@id,@code,@typeId,@clientId,@actor,'WAITING')",
            new{id,code,typeId,clientId,actor},tx);
        await tx.CommitAsync();
        await audit.WriteAsync(actor,"CREATE","turns",id.ToString(),new{code,typeId});
        return new{id,code,status="WAITING"};
    }
    public async Task UpdateStatusAsync(Guid id, string status, Guid actor) {
        var allowed=new[]{"WAITING","CALLED","IN_PROGRESS","COMPLETED","CANCELLED","NO_SHOW"};
        if(!allowed.Contains(status)) throw new ArgumentException("Estado de turno no válido.");
        await using var db=await Db.Source(config).OpenConnectionAsync();
        var n=await db.ExecuteAsync("""
          update app.turns set status=@status,
          called_at=case when @status='CALLED' then now() else called_at end,
          started_at=case when @status='IN_PROGRESS' then now() else started_at end,
          completed_at=case when @status='COMPLETED' then now() else completed_at end
          where id=@id and deleted_at is null
          """,new{id,status});
        if(n==0) throw new KeyNotFoundException("Turno no encontrado.");
        await audit.WriteAsync(actor,"UPDATE_STATUS","turns",id.ToString(),new{status});
    }
}

public sealed class ClientService(IConfiguration config, DataProtectionService crypto, AuditService audit)
{
    public async Task<IEnumerable<object>> ListAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("""
          select id,first_name as "firstName",last_name as "lastName",phone,address,address_reference as "addressReference",is_active as "isActive",created_at as "createdAt"
          from app.clients where deleted_at is null order by created_at desc limit 1000
          """);
    }
    public async Task<Guid> CreateAsync(ClientRequest x, Guid actor) {
        if(x.Identification.Length is <10 or >13 || !x.Identification.All(char.IsDigit)) throw new ArgumentException("La identificación debe contener entre 10 y 13 dígitos.");
        if(!System.Text.RegularExpressions.Regex.IsMatch(x.Phone,@"^09[0-9]{8,11}$")) throw new ArgumentException("El teléfono debe iniciar en 09 y contener al menos 10 dígitos.");
        if(x.Address.Length is <20 or >100 || x.AddressReference.Length is <20 or >100) throw new ArgumentException("Dirección y referencia deben contener entre 20 y 100 caracteres.");
        await using var db=await Db.Source(config).OpenConnectionAsync();
        var id=Guid.NewGuid();
        await db.ExecuteAsync("""
          insert into app.clients(id,first_name,last_name,identification_cipher,identification_lookup,email_cipher,email_lookup,phone,address,address_reference,created_by)
          values(@id,@first,@last,@identCipher,@identLookup,@emailCipher,@emailLookup,@phone,@address,@reference,@actor)
          """,new{id,first=x.FirstName.Trim(),last=x.LastName.Trim(),identCipher=crypto.Encrypt(x.Identification),identLookup=crypto.Lookup(x.Identification),
            emailCipher=crypto.Encrypt(x.Email),emailLookup=crypto.Lookup(x.Email),x.Phone,x.Address,x.AddressReference,actor});
        await audit.WriteAsync(actor,"CREATE","clients",id.ToString());
        return id;
    }
}
public sealed record ClientRequest(string FirstName,string LastName,string Identification,string Email,string Phone,string Address,string AddressReference);

public sealed class CashBoxService(IConfiguration config, AuditService audit)
{
    public async Task<IEnumerable<object>> ListAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("""
          select b.id,b.code,b.name,b.description,b.is_active as "isActive",
          count(a.id) filter(where a.is_active and a.unassigned_at is null) as "assignedCashiers"
          from app.cash_boxes b left join app.cash_box_assignments a on a.cash_box_id=b.id
          where b.deleted_at is null group by b.id order by b.name
          """);
    }
    public async Task<Guid> CreateAsync(string code,string name,string? description,Guid actor) {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        var id=Guid.NewGuid();
        await db.ExecuteAsync("insert into app.cash_boxes(id,code,name,description) values(@id,@code,@name,@description)",new{id,code,name,description});
        await audit.WriteAsync(actor,"CREATE","cash_boxes",id.ToString());
        return id;
    }
    public async Task AssignAsync(Guid boxId,Guid cashierId,Guid actor) {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        await using var tx=await db.BeginTransactionAsync();
        await db.ExecuteAsync("select id from app.cash_boxes where id=@boxId for update",new{boxId},tx);
        await db.ExecuteAsync("insert into app.cash_box_assignments(cash_box_id,cashier_user_id,assigned_by) values(@boxId,@cashierId,@actor)",new{boxId,cashierId,actor},tx);
        await tx.CommitAsync();
        await audit.WriteAsync(actor,"ASSIGN_CASHIER","cash_box_assignments",boxId.ToString(),new{cashierId});
    }
}

public sealed class BusinessService(IConfiguration config, AuditService audit)
{
    public async Task<IEnumerable<object>> ServicesAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("select id,name,description,equipment,speed_mbps as 'speedMbps',price,is_active as 'isActive' from app.internet_services where deleted_at is null order by name");
    }
    public async Task<Guid> CreateServiceAsync(ServiceRequest x,Guid actor) {
        if(x.SpeedMbps<=0 || x.Price<0) throw new ArgumentException("Velocidad y precio inválidos.");
        await using var db=await Db.Source(config).OpenConnectionAsync(); var id=Guid.NewGuid();
        await db.ExecuteAsync("insert into app.internet_services(id,name,description,equipment,speed_mbps,price) values(@id,@name,@description,@equipment,@speedMbps,@price)",new{id,x.Name,x.Description,x.Equipment,x.SpeedMbps,x.Price});
        await audit.WriteAsync(actor,"CREATE","internet_services",id.ToString()); return id;
    }
    public async Task<IEnumerable<object>> ContractsAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("""
          select c.id,c.contract_number as "contractNumber",c.client_id as "clientId",s.name as service,c.payment_method as "paymentMethod",
          c.status,c.start_date as "startDate",c.end_date as "endDate",c.replaced_contract_id as "replacedContractId"
          from app.contracts c join app.internet_services s on s.id=c.service_id where c.deleted_at is null order by c.created_at desc
          """);
    }
    public async Task<IEnumerable<object>> PaymentsAsync() {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("""
          select p.id,p.contract_id as "contractId",p.amount,p.payment_method as "paymentMethod",p.payment_reference as "paymentReference",
          p.status,p.paid_at as "paidAt",p.created_at as "createdAt" from app.payments p where p.deleted_at is null order by p.created_at desc
          """);
    }
    public async Task<object> CreateContractAsync(ContractRequest x,Guid actor) {
        await using var db=await Db.Source(config).OpenConnectionAsync(); var id=Guid.NewGuid();
        await db.ExecuteAsync("""
          insert into app.contracts(id,contract_number,client_id,service_id,payment_method,status,created_by)
          values(@id,@number,@client,@service,@method,'VIG',@actor)
          """,new{id,number=x.ContractNumber,client=x.ClientId,service=x.ServiceId,method=x.PaymentMethod,actor});
        await audit.WriteAsync(actor,"CREATE","contracts",id.ToString()); return new{id,contractNumber=x.ContractNumber,status="VIG"};
    }
    public async Task ReplaceContractAsync(Guid oldId,ContractRequest x,Guid actor) {
        await using var db=await Db.Source(config).OpenConnectionAsync(); await using var tx=await db.BeginTransactionAsync();
        var old=await db.QuerySingleOrDefaultAsync<OldContract>("select client_id as ClientId,end_date as EndDate from app.contracts where id=@oldId and deleted_at is null for update",new{oldId},tx);
        if(old is null) throw new KeyNotFoundException("Contrato vigente no encontrado.");
        await db.ExecuteAsync("update app.contracts set status='SUS',end_date=coalesce(end_date,current_date),updated_at=now() where id=@oldId",new{oldId},tx);
        var id=Guid.NewGuid();
        await db.ExecuteAsync("""
          insert into app.contracts(id,contract_number,client_id,service_id,payment_method,status,start_date,end_date,replaced_contract_id,created_by)
          values(@id,@number,@client,@service,@method,'VIG',current_date,@oldEnd,@oldId,@actor)
          """,new{id,number=x.ContractNumber,client=old.ClientId,service=x.ServiceId,method=x.PaymentMethod,oldEnd=old.EndDate,oldId,actor},tx);
        await tx.CommitAsync(); await audit.WriteAsync(actor,"REPLACE","contracts",id.ToString(),new{oldId});
    }
    public async Task CancelContractAsync(Guid id,Guid actor) {
        await using var db=await Db.Source(config).OpenConnectionAsync();
        var n=await db.ExecuteAsync("update app.contracts set status='CAN',end_date=current_date,updated_at=now() where id=@id and status='VIG' and deleted_at is null",new{id});
        if(n==0) throw new KeyNotFoundException("Contrato vigente no encontrado.");
        await audit.WriteAsync(actor,"CANCEL","contracts",id.ToString());
    }
    public async Task<Guid> CreatePaymentAsync(PaymentRequest x,Guid actor) {
        if(x.Amount<=0) throw new ArgumentException("El monto debe ser mayor que cero.");
        await using var db=await Db.Source(config).OpenConnectionAsync(); var id=Guid.NewGuid();
        await db.ExecuteAsync("insert into app.payments(id,contract_id,amount,payment_method,payment_reference,status,paid_at,received_by) values(@id,@contractId,@amount,@method,@reference,'PAID',now(),@actor)",new{id,contractId=x.ContractId,amount=x.Amount,method=x.PaymentMethod,reference=x.Reference,actor});
        await audit.WriteAsync(actor,"CREATE","payments",id.ToString()); return id;
    }
    private sealed record OldContract(Guid ClientId,DateTime? EndDate);
}
public sealed record ServiceRequest(string Name,string Description,string Equipment,int SpeedMbps,decimal Price);
public sealed record ContractRequest(string ContractNumber,Guid ClientId,Guid ServiceId,string PaymentMethod);
public sealed record PaymentRequest(Guid ContractId,decimal Amount,string PaymentMethod,string? Reference);