-- Установочный снимок SQLite с демонстрационными данными.
PRAGMA foreign_keys = OFF;
PRAGMA user_version = 4;
BEGIN TRANSACTION;
CREATE TABLE bookings (
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
INSERT INTO "bookings" VALUES(1001,1,1,2,1,13000000,'new','','2026-11-18T14:17:44.4921486+00:00');
INSERT INTO "bookings" VALUES(1002,2,2,2,1,18000000,'new','','2026-11-18T14:17:44.4924974+00:00');
INSERT INTO "bookings" VALUES(1003,3,3,2,1,21000000,'new','','2026-11-18T14:17:44.4926827+00:00');
INSERT INTO "bookings" VALUES(1004,4,1,2,1,13000000,'new','','2026-11-18T14:17:44.4927409+00:00');
INSERT INTO "bookings" VALUES(1005,5,2,2,1,18000000,'new','','2026-11-18T14:17:44.4927914+00:00');
INSERT INTO "bookings" VALUES(1006,6,3,2,1,21000000,'new','','2026-11-18T14:17:44.4928378+00:00');
CREATE TABLE clients (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    last_name TEXT NOT NULL CHECK(length(last_name) BETWEEN 1 AND 100),
    first_name TEXT NOT NULL CHECK(length(first_name) BETWEEN 1 AND 100),
    patronymic TEXT NOT NULL DEFAULT '' CHECK(length(patronymic) <= 100),
    phone TEXT NOT NULL CHECK(length(phone) BETWEEN 8 AND 16),
    email TEXT NOT NULL DEFAULT '' CHECK(length(email) <= 254),
    comments TEXT NOT NULL DEFAULT '' CHECK(length(comments) <= 2000),
    search_name TEXT NOT NULL
);
INSERT INTO "clients" VALUES(1,'Миронова','Александра','Игоревна','+79000000001','client1@example.invalid','Демонстрационные данные. Все сведения вымышлены.','МИРОНОВА АЛЕКСАНДРА ИГОРЕВНА');
INSERT INTO "clients" VALUES(2,'Соколов','Артём','Сергеевич','+79000000002','client2@example.invalid','Демонстрационные данные. Все сведения вымышлены.','СОКОЛОВ АРТЁМ СЕРГЕЕВИЧ');
INSERT INTO "clients" VALUES(3,'Волкова','Мария','Алексеевна','+79000000003','client3@example.invalid','Демонстрационные данные. Все сведения вымышлены.','ВОЛКОВА МАРИЯ АЛЕКСЕЕВНА');
INSERT INTO "clients" VALUES(4,'Кузнецов','Дмитрий','Олегович','+79000000004','client4@example.invalid','Демонстрационные данные. Все сведения вымышлены.','КУЗНЕЦОВ ДМИТРИЙ ОЛЕГОВИЧ');
INSERT INTO "clients" VALUES(5,'Орлова','Елена','Викторовна','+79000000005','client5@example.invalid','Демонстрационные данные. Все сведения вымышлены.','ОРЛОВА ЕЛЕНА ВИКТОРОВНА');
INSERT INTO "clients" VALUES(6,'Новиков','Илья','Андреевич','+79000000006','client6@example.invalid','Демонстрационные данные. Все сведения вымышлены.','НОВИКОВ ИЛЬЯ АНДРЕЕВИЧ');
CREATE TABLE countries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);
INSERT INTO "countries" VALUES(1,'Россия');
INSERT INTO "countries" VALUES(2,'Турция');
INSERT INTO "countries" VALUES(3,'Египет');
CREATE TABLE destinations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    country_id INTEGER NOT NULL REFERENCES countries(id) ON DELETE RESTRICT,
    city_name TEXT NOT NULL,
    UNIQUE(country_id, city_name)
);
INSERT INTO "destinations" VALUES(1,1,'Сочи');
INSERT INTO "destinations" VALUES(2,2,'Анталья');
INSERT INTO "destinations" VALUES(3,3,'Хургада');
CREATE TABLE employees (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL UNIQUE CHECK(length(username) BETWEEN 1 AND 100),
    full_name TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL CHECK(role IN ('admin','manager')),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1))
, password_new TEXT NOT NULL DEFAULT '');
INSERT INTO "employees" VALUES(1,'admin','Ирина Лебедева','AQAAAAIAAYagAAAAEF+ansS6Zqt8ETQsWc/m02WE0U/zP/Wx0X8mh9hb8L4RoM5rbhBq/aKsv4I1qdnALg==','admin',1,'');
INSERT INTO "employees" VALUES(2,'manager','Анна Морозова','AQAAAAIAAYagAAAAEOUBnfpuvWFDYJfU+JyUyAmZiEYhjq+ob07hLi2qVaV86NeaHexqo2ukgn7zBPij5w==','manager',1,'');
CREATE TABLE hotels (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    destination_id INTEGER NOT NULL REFERENCES destinations(id) ON DELETE RESTRICT,
    name TEXT NOT NULL,
    UNIQUE(destination_id, name)
);
INSERT INTO "hotels" VALUES(1,1,'Морской парк');
INSERT INTO "hotels" VALUES(2,2,'Лазурный берег');
INSERT INTO "hotels" VALUES(3,3,'Солнечная бухта');
CREATE TABLE tour_operators (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);
INSERT INTO "tour_operators" VALUES(1,'Вектор путешествий — демо');
CREATE TABLE tours (
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
INSERT INTO "tours" VALUES(1,'Неделя у моря',1,1,'2026-12-09','2026-12-16','Проживание 7 ночей, завтрак, трансфер. Учебное предложение.',13000000,1);
INSERT INTO "tours" VALUES(2,'Анталья без спешки',2,1,'2026-12-23','2026-12-30','Проживание 7 ночей, завтрак, трансфер. Учебное предложение.',18000000,1);
INSERT INTO "tours" VALUES(3,'Солнце Красного моря',3,1,'2027-01-07','2027-01-14','Проживание 7 ночей, завтрак, трансфер. Учебное предложение.',21000000,1);
INSERT INTO "tours" VALUES(4,'Сочи — архивное предложение',1,1,'2026-10-24','2026-10-31','Проживание 7 ночей, завтрак, трансфер. Учебное предложение.',9500000,0);
CREATE INDEX clients_phone_idx ON clients(phone);
CREATE INDEX clients_name_idx ON clients(search_name);
CREATE INDEX tours_available_idx ON tours(is_active, starts_on);
CREATE INDEX bookings_client_idx ON bookings(client_id);
CREATE INDEX bookings_manager_status_idx ON bookings(manager_id, status);
DELETE FROM "sqlite_sequence";
INSERT INTO "sqlite_sequence" VALUES('employees',2);
INSERT INTO "sqlite_sequence" VALUES('countries',3);
INSERT INTO "sqlite_sequence" VALUES('destinations',3);
INSERT INTO "sqlite_sequence" VALUES('hotels',3);
INSERT INTO "sqlite_sequence" VALUES('tour_operators',1);
INSERT INTO "sqlite_sequence" VALUES('tours',4);
INSERT INTO "sqlite_sequence" VALUES('clients',6);
INSERT INTO "sqlite_sequence" VALUES('bookings',1006);
COMMIT;
PRAGMA foreign_keys = ON;
