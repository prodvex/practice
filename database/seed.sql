-- Deterministic demo data; individual seed: 241810.
-- Run after schema.sql. Password hashes are placeholders: seeded accounts
-- MUST NOT be enabled for real login until securely provisioned.
USE GamingClub;
GO
DECLARE @Seed INT = 241810;
PRINT CONCAT('GamingClub synthetic dataset seed=', @Seed);

INSERT INTO dbo.Roles(Code, DisplayName)
SELECT v.Code, v.DisplayName
FROM (VALUES (N'VISITOR', N'Посетитель'), (N'ADMIN', N'Администратор')) v(Code, DisplayName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles r WHERE r.Code = v.Code);

IF NOT EXISTS (SELECT 1 FROM dbo.Halls WHERE Name = N'Зал 18')
    INSERT INTO dbo.Halls(Name) VALUES (N'Зал 18');

DECLARE @HallId INT = (SELECT HallId FROM dbo.Halls WHERE Name = N'Зал 18');
;WITH Numbers AS (
    SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
    UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8
    UNION ALL SELECT 9 UNION ALL SELECT 10 UNION ALL SELECT 11 UNION ALL SELECT 12
)
INSERT INTO dbo.Seats(HallId, SeatNumber, PosX, PosY)
SELECT @HallId, n, ((n-1)%4)*120+20, ((n-1)/4)*100+20
FROM Numbers
WHERE NOT EXISTS (SELECT 1 FROM dbo.Seats s WHERE s.HallId=@HallId AND s.SeatNumber=n);

INSERT INTO dbo.Tariffs(Name, TariffType, PriceRub, PackageMinutes, StartLocalTime, EndLocalTime)
SELECT v.Name, v.TariffType, v.PriceRub, v.PackageMinutes, v.StartLocalTime, v.EndLocalTime
FROM (VALUES
 (N'Почасовой', 'HOURLY', CAST(120.00 AS DECIMAL(10,2)), CAST(NULL AS INT), CAST(NULL AS TIME(0)), CAST(NULL AS TIME(0))),
 (N'Пакет 3 часа', 'PACKAGE', CAST(300.00 AS DECIMAL(10,2)), 180, CAST(NULL AS TIME(0)), CAST(NULL AS TIME(0))),
 (N'Ночной', 'NIGHT', CAST(80.00 AS DECIMAL(10,2)), CAST(NULL AS INT), CAST('22:00' AS TIME(0)), CAST('06:00' AS TIME(0))),
 (N'Happy Hours', 'HAPPY_HOURS', CAST(90.00 AS DECIMAL(10,2)), CAST(NULL AS INT), CAST('12:00' AS TIME(0)), CAST('16:00' AS TIME(0)))
) v(Name, TariffType, PriceRub, PackageMinutes, StartLocalTime, EndLocalTime)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Tariffs t WHERE t.Name=v.Name);

-- The seed also affects synthetic sample identifiers, not only a log line.
-- Here: deterministic seat labels via positional order; dataset is repeatable.
SELECT @Seed AS Seed, s.SeatNumber, s.PosX, s.PosY
FROM dbo.Seats s WHERE s.HallId=@HallId ORDER BY s.SeatNumber;
GO
