-- Sistema de gestion de turnos
-- PostgreSQL / Supabase
-- Ejecutar en Supabase SQL Editor sobre una base nueva.
-- Datos personales sensibles: guardar cifrados desde el backend.
-- No guardar contrasenas en texto plano: usar hash Argon2id o BCrypt desde .NET.

begin;

create extension if not exists pgcrypto;

create schema if not exists app;

-- Roles del sistema
create table if not exists app.roles (
    id smallint generated always as identity primary key,
    code varchar(20) not null unique,
    name varchar(60) not null,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    constraint ck_roles_code check (code in ('ADMINISTRADOR','GESTOR','CAJERO'))
);

insert into app.roles (code, name)
values ('ADMINISTRADOR','Administrador'),
       ('GESTOR','Gestor'),
       ('CAJERO','Cajero')
on conflict (code) do nothing;

-- Usuarios internos. identification_cipher y email_cipher son cifrados por la API.
-- identification_lookup y email_lookup son HMAC/SHA-256 normalizados para buscar duplicados.
create table if not exists app.users (
    id uuid primary key default gen_random_uuid(),
    role_id smallint not null references app.roles(id),
    username varchar(20) not null unique,
    password_hash text not null,
    identification_cipher bytea not null,
    identification_lookup bytea not null unique,
    email_cipher bytea not null,
    email_lookup bytea not null unique,
    first_name varchar(100) not null,
    last_name varchar(100) not null,
    status varchar(20) not null default 'PENDING',
    is_active boolean not null default true,
    is_blocked boolean not null default false,
    failed_login_attempts smallint not null default 0,
    last_login_at timestamptz,
    created_by uuid references app.users(id),
    approved_by uuid references app.users(id),
    approved_at timestamptz,
    deleted_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_users_username check (username ~ '^[A-Za-z0-9]{8,20}$' and username ~ '[A-Za-z]' and username ~ '[0-9]'),
    constraint ck_users_status check (status in ('PENDING','APPROVED','REJECTED','INACTIVE')),
    constraint ck_users_failed_attempts check (failed_login_attempts >= 0)
);

create index if not exists ix_users_role_status on app.users(role_id, status) where deleted_at is null;

-- Cajas
create table if not exists app.cash_boxes (
    id uuid primary key default gen_random_uuid(),
    code varchar(20) not null unique,
    name varchar(100) not null,
    description varchar(250),
    is_active boolean not null default true,
    deleted_at timestamptz,
    created_at timestamptz not null default now()
);

-- Asignacion de cajeros a cajas. Maximo 2 cajeros activos por caja se valida con trigger.
create table if not exists app.cash_box_assignments (
    id uuid primary key default gen_random_uuid(),
    cash_box_id uuid not null references app.cash_boxes(id),
    cashier_user_id uuid not null references app.users(id),
    assigned_by uuid not null references app.users(id),
    assigned_at timestamptz not null default now(),
    unassigned_at timestamptz,
    is_active boolean not null default true,
    constraint uq_cashbox_cashier_assignment unique (cash_box_id, cashier_user_id, assigned_at)
);

create index if not exists ix_cash_box_assignments_active on app.cash_box_assignments(cash_box_id) where is_active;

create or replace function app.enforce_max_two_cashiers_per_box()
returns trigger
language plpgsql
as $$
declare
    active_count integer;
    cashier_role_id smallint;
begin
    if new.is_active and new.unassigned_at is null then
        select count(*)
          into active_count
          from app.cash_box_assignments a
         where a.cash_box_id = new.cash_box_id
           and a.is_active
           and a.unassigned_at is null
           and a.id <> coalesce(new.id, '00000000-0000-0000-0000-000000000000'::uuid);

        if active_count >= 2 then
            raise exception 'Una caja puede tener maximo 2 cajeros asignados';
        end if;

        select role_id into cashier_role_id
          from app.users
         where id = new.cashier_user_id
           and deleted_at is null
           and is_active
           and status = 'APPROVED';

        if cashier_role_id is null or not exists (
            select 1 from app.roles r
             where r.id = cashier_role_id and r.code = 'CAJERO'
        ) then
            raise exception 'La asignacion requiere un usuario cajero aprobado y activo';
        end if;
    end if;
    return new;
end;
$$;

drop trigger if exists trg_max_two_cashiers_per_box on app.cash_box_assignments;
create trigger trg_max_two_cashiers_per_box
before insert or update of cash_box_id, cashier_user_id, is_active, unassigned_at
on app.cash_box_assignments
for each row execute function app.enforce_max_two_cashiers_per_box();

-- Tipos de atencion y contador atomico para los codigos de turnos.
create table if not exists app.attention_types (
    id smallint generated always as identity primary key,
    code_prefix char(2) not null unique,
    name varchar(100) not null,
    description varchar(250),
    next_number integer not null default 1,
    is_active boolean not null default true,
    constraint ck_attention_prefix check (code_prefix ~ '^[A-Z]{2}$'),
    constraint ck_attention_next_number check (next_number between 1 and 10000)
);

insert into app.attention_types (code_prefix, name, description)
values ('AC','Atencion al cliente','Consultas y mantenimiento de datos del cliente'),
       ('PS','Pago de servicio','Pagos de servicios y contratos'),
       ('CS','Contratacion de servicio','Contratacion de internet'),
       ('CV','Cambio de servicio','Cambio de plan contratado'),
       ('CP','Cambio de pago','Cambio de forma de pago'),
       ('CA','Cancelacion de contrato','Cancelacion de servicio')
on conflict (code_prefix) do nothing;

create table if not exists app.turns (
    id uuid primary key default gen_random_uuid(),
    code varchar(6) not null unique,
    attention_type_id smallint not null references app.attention_types(id),
    client_id uuid,
    cash_box_id uuid references app.cash_boxes(id),
    assigned_by uuid not null references app.users(id),
    cashier_user_id uuid references app.users(id),
    status varchar(20) not null default 'ASSIGNED',
    assigned_at timestamptz not null default now(),
    called_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    notes varchar(500),
    deleted_at timestamptz,
    constraint ck_turn_code check (code ~ '^[A-Z]{2}[0-9]{4}$'),
    constraint ck_turn_status check (status in ('ASSIGNED','WAITING','CALLED','IN_PROGRESS','COMPLETED','CANCELLED','NO_SHOW'))
);

create index if not exists ix_turns_assigned_by_date on app.turns(assigned_by, assigned_at);
create index if not exists ix_turns_cashier_date on app.turns(cashier_user_id, assigned_at);
create index if not exists ix_turns_box_status on app.turns(cash_box_id, status) where deleted_at is null;

-- Funcion SQL requerida por la prueba: genera codigo de turno de forma atomica.
create or replace function app.generate_turn_code(p_attention_type_id smallint)
returns varchar(6)
language plpgsql
as $$
declare
    v_prefix char(2);
    v_number integer;
begin
    update app.attention_types
       set next_number = next_number + 1
     where id = p_attention_type_id
       and is_active
       and next_number <= 9999
    returning code_prefix, next_number - 1
         into v_prefix, v_number;

    if not found then
        raise exception 'Tipo de atencion inexistente, inactivo o sin numeros disponibles';
    end if;

    return v_prefix || lpad(v_number::text, 4, '0');
end;
$$;

-- Clientes: datos identificativos cifrados por la API; hash/HMAC para busqueda exacta.
create table if not exists app.clients (
    id uuid primary key default gen_random_uuid(),
    first_name varchar(100) not null,
    last_name varchar(100) not null,
    identification_cipher bytea not null,
    identification_lookup bytea not null unique,
    email_cipher bytea not null,
    email_lookup bytea not null,
    phone varchar(13) not null,
    address varchar(100) not null,
    address_reference varchar(100) not null,
    is_active boolean not null default true,
    deleted_at timestamptz,
    created_by uuid not null references app.users(id),
    updated_by uuid references app.users(id),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_clients_identification_length check (octet_length(identification_lookup) > 0),
    constraint ck_clients_phone check (phone ~ '^09[0-9]{8,11}$'),
    constraint ck_clients_address_length check (char_length(address) between 20 and 100),
    constraint ck_clients_reference_length check (char_length(address_reference) between 20 and 100)
);

create index if not exists ix_clients_name on app.clients(last_name, first_name) where deleted_at is null;

alter table app.turns
    drop constraint if exists fk_turns_client;
alter table app.turns
    add constraint fk_turns_client foreign key (client_id) references app.clients(id);

-- Catalogo de servicios de internet
create table if not exists app.internet_services (
    id uuid primary key default gen_random_uuid(),
    name varchar(120) not null unique,
    description varchar(500) not null,
    equipment text not null,
    speed_mbps integer not null,
    price numeric(10,2) not null,
    is_active boolean not null default true,
    deleted_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_service_speed check (speed_mbps > 0),
    constraint ck_service_price check (price >= 0)
);

-- Contratos: al cambiar de plan, el anterior se sustituye y se crea un nuevo contrato.
create table if not exists app.contracts (
    id uuid primary key default gen_random_uuid(),
    contract_number varchar(30) not null unique,
    client_id uuid not null references app.clients(id),
    service_id uuid not null references app.internet_services(id),
    payment_method varchar(20) not null,
    status varchar(20) not null default 'VIG',
    start_date date not null default current_date,
    end_date date,
    replaced_contract_id uuid references app.contracts(id),
    created_by uuid not null references app.users(id),
    deleted_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_contract_payment_method check (payment_method in ('EFECTIVO','TARJETA','TRANSFERENCIA','DEBITO_AUTOMATICO')),
    constraint ck_contract_status check (status in ('VIG','SUS','CAN','VEN','PEN')),
    constraint ck_contract_dates check (end_date is null or end_date >= start_date)
);

create index if not exists ix_contracts_client_status on app.contracts(client_id, status) where deleted_at is null;

-- Pagos asociados a contratos
create table if not exists app.payments (
    id uuid primary key default gen_random_uuid(),
    contract_id uuid not null references app.contracts(id),
    amount numeric(10,2) not null,
    payment_method varchar(20) not null,
    payment_reference varchar(100),
    status varchar(20) not null default 'PENDING',
    paid_at timestamptz,
    received_by uuid not null references app.users(id),
    created_at timestamptz not null default now(),
    deleted_at timestamptz,
    constraint ck_payments_amount check (amount > 0),
    constraint ck_payments_method check (payment_method in ('EFECTIVO','TARJETA','TRANSFERENCIA','DEBITO_AUTOMATICO')),
    constraint ck_payments_status check (status in ('PENDING','PAID','FAILED','CANCELLED'))
);

create index if not exists ix_payments_contract_date on app.payments(contract_id, created_at);

-- Registro de sesion para indicadores administrativos.
create table if not exists app.user_sessions (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references app.users(id),
    login_at timestamptz not null default now(),
    last_activity_at timestamptz not null default now(),
    logout_at timestamptz,
    ip_address inet,
    user_agent varchar(500),
    is_active boolean not null default true
);

create index if not exists ix_user_sessions_active on app.user_sessions(user_id, is_active);

-- Auditoria funcional basica. Serilog escribira ademas a los sinks configurados por la API.
create table if not exists app.audit_events (
    id bigint generated always as identity primary key,
    actor_user_id uuid references app.users(id),
    action varchar(80) not null,
    entity_name varchar(100) not null,
    entity_id varchar(100),
    details jsonb,
    created_at timestamptz not null default now()
);

create index if not exists ix_audit_events_created_at on app.audit_events(created_at desc);
create index if not exists ix_audit_events_actor_date on app.audit_events(actor_user_id, created_at desc);

commit;

-- Nota de seguridad:
-- 1. Configurar RLS y privilegios antes de exponer tablas por PostgREST.
-- 2. La API debe usar credenciales de backend; nunca exponer claves privilegiadas al navegador.
-- 3. Cifrar identification_cipher/email_cipher en .NET con AES-GCM y clave fuera de la BD.
-- 4. identification_lookup/email_lookup deben ser HMAC-SHA-256 con clave secreta del backend,
--    no hashes simples, para reducir ataques de diccionario a identificaciones/correos.
-- 5. El trigger limita asignaciones, pero la API tambien debe serializar operaciones de caja
--    para garantizar que solo un cajero atienda activamente por caja a la vez.
