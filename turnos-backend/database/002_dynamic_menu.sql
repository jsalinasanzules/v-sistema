-- Menu dinamico por rol. La API consulta estas tablas; no codifica menus por rol.
begin;

create table if not exists app.menu_items (
    id integer generated always as identity primary key,
    parent_id integer references app.menu_items(id),
    code varchar(60) not null unique,
    label varchar(100) not null,
    route varchar(200),
    icon varchar(60),
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamptz not null default now()
);

create table if not exists app.role_menu_permissions (
    role_id smallint not null references app.roles(id) on delete cascade,
    menu_item_id integer not null references app.menu_items(id) on delete cascade,
    can_view boolean not null default true,
    can_create boolean not null default false,
    can_update boolean not null default false,
    can_delete boolean not null default false,
    primary key (role_id, menu_item_id)
);

insert into app.menu_items (code,label,route,icon,sort_order) values
('dashboard','Inicio','/dashboard','dashboard',10),
('users','Usuarios','/admin/usuarios','people',20),
('approvals','Aprobaciones','/gestor/aprobaciones','approval',30),
('cashboxes','Cajas','/gestor/cajas','point_of_sale',40),
('turns','Turnos','/turnos','confirmation_number',50),
('clients','Clientes','/clientes','groups',60),
('services','Servicios','/servicios','router',70),
('contracts','Contratos','/contratos','description',80),
('payments','Pagos','/pagos','payments',90),
('reports','Reportes','/reportes','analytics',100)
on conflict (code) do nothing;

-- Permisos iniciales: ajustar en base a los requerimientos de negocio.
-- Admin: todo. Gestor: usuarios (crear solo, pendiente de aprobacion), aprobaciones, cajas, turnos, clientes, servicios, contratos, pagos y reportes.
-- Cajero: dashboard, turnos, clientes, contratos, pagos.
insert into app.role_menu_permissions(role_id, menu_item_id, can_view, can_create, can_update, can_delete)
select r.id, m.id, true,
       (r.code = 'ADMINISTRADOR' or (r.code = 'GESTOR' and m.code in ('users','cashboxes','turns','clients','services','contracts','payments')) or (r.code = 'CAJERO' and m.code in ('turns','clients','payments'))),
       (r.code = 'ADMINISTRADOR' or (r.code = 'GESTOR' and m.code in ('cashboxes','turns','clients','services','contracts','payments')) or (r.code = 'CAJERO' and m.code in ('turns','clients','payments'))),
       (r.code = 'ADMINISTRADOR')
from app.roles r cross join app.menu_items m
on conflict (role_id, menu_item_id) do nothing;

-- Dashboard disponible para todos los roles.
update app.role_menu_permissions p
set can_view = true
from app.menu_items m
where p.menu_item_id = m.id and m.code = 'dashboard';

commit;
