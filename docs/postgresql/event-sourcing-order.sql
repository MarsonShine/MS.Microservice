create schema if not exists event_sourcing;

create table if not exists event_sourcing.event_store (
    global_position bigserial primary key,
    event_id uuid not null unique,
    stream_id varchar(200) not null,
    stream_type varchar(100) not null,
    version int not null,
    event_type varchar(200) not null,
    payload jsonb not null,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now()
);

create unique index if not exists uq_event_store_stream_version
    on event_sourcing.event_store(stream_id, version);

create index if not exists ix_event_store_stream_id
    on event_sourcing.event_store(stream_id);

create index if not exists ix_event_store_stream_type
    on event_sourcing.event_store(stream_type);

create index if not exists ix_event_store_created_at
    on event_sourcing.event_store(created_at);

create table if not exists event_sourcing.snapshots (
    stream_id varchar(200) primary key,
    stream_type varchar(100) not null,
    version int not null,
    state jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists event_sourcing.projection_checkpoint (
    projection_name varchar(200) primary key,
    last_global_position bigint not null,
    updated_at timestamptz not null default now()
);

create table if not exists event_sourcing.order_read_model (
    order_id varchar(200) primary key,
    customer_id varchar(200) not null,
    currency varchar(20) not null,
    status varchar(50) not null,
    item_count int not null default 0,
    total_amount numeric(18, 2) not null default 0,
    version int not null default 0,
    updated_at timestamptz not null default now()
);
