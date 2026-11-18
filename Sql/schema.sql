PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS employees (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL UNIQUE CHECK(length(username) BETWEEN 1 AND 100),
    full_name TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    password_new TEXT NOT NULL DEFAULT '',
    role TEXT NOT NULL CHECK(role IN ('admin','manager')),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1))
);
CREATE TABLE IF NOT EXISTS clients (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    last_name TEXT NOT NULL CHECK(length(last_name) BETWEEN 1 AND 100),
    first_name TEXT NOT NULL CHECK(length(first_name) BETWEEN 1 AND 100),
    patronymic TEXT NOT NULL DEFAULT '' CHECK(length(patronymic) <= 100),
    phone TEXT NOT NULL CHECK(length(phone) BETWEEN 8 AND 16),
    email TEXT NOT NULL DEFAULT '' CHECK(length(email) <= 254),
    comments TEXT NOT NULL DEFAULT '' CHECK(length(comments) <= 2000),
    search_name TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS clients_phone_idx ON clients(phone);
CREATE INDEX IF NOT EXISTS clients_name_idx ON clients(search_name);
CREATE TABLE IF NOT EXISTS countries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);
CREATE TABLE IF NOT EXISTS destinations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    country_id INTEGER NOT NULL REFERENCES countries(id) ON DELETE RESTRICT,
    city_name TEXT NOT NULL,
    UNIQUE(country_id, city_name)
);
CREATE TABLE IF NOT EXISTS hotels (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    destination_id INTEGER NOT NULL REFERENCES destinations(id) ON DELETE RESTRICT,
    name TEXT NOT NULL,
    UNIQUE(destination_id, name)
);
CREATE TABLE IF NOT EXISTS tour_operators (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);
CREATE TABLE IF NOT EXISTS tours (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL CHECK(length(title) BETWEEN 1 AND 200),
    hotel_id INTEGER NOT NULL REFERENCES hotels(id) ON DELETE RESTRICT,
    operator_id INTEGER NOT NULL REFERENCES tour_operators(id) ON DELETE RESTRICT,
    starts_on TEXT NOT NULL CHECK(length(starts_on) = 10),
    ends_on TEXT NOT NULL CHECK(length(ends_on) = 10 AND ends_on >= starts_on),
    services TEXT NOT NULL DEFAULT '' CHECK(length(services) <= 4000),
    price_kopecks INTEGER NOT NULL CHECK(typeof(price_kopecks) = 'integer' AND price_kopecks BETWEEN 1 AND 999999999999),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1))
);
CREATE INDEX IF NOT EXISTS tours_available_idx ON tours(is_active, starts_on);
CREATE TABLE IF NOT EXISTS bookings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE RESTRICT,
    tour_id INTEGER NOT NULL REFERENCES tours(id) ON DELETE RESTRICT,
    manager_id INTEGER NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    travelers_count INTEGER NOT NULL CHECK(travelers_count BETWEEN 1 AND 100),
    total_price_kopecks INTEGER NOT NULL CHECK(typeof(total_price_kopecks) = 'integer' AND total_price_kopecks BETWEEN 1 AND 999999999999),
    status TEXT NOT NULL DEFAULT 'new' CHECK(status IN ('new','in_progress','awaiting_payment','completed','cancelled')),
    cancel_reason TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS bookings_client_idx ON bookings(client_id);
CREATE INDEX IF NOT EXISTS bookings_manager_status_idx ON bookings(manager_id, status);
