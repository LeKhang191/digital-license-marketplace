-- Real cheap Steam demo games dataset (~15,000 - 45,000 VND Steam Global keys)
-- Works even when ISP blocks store.steampowered.com API.
-- Apply with:
--   docker compose exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d catalog_db' < datasets/demo/real-demo-games.sql

BEGIN;

INSERT INTO products (
    "Id", "Name", "Slug", "Description", "Category", "Price",
    "IsActive", "CreatedAtUtc", "UpdatedAtUtc", "SteamMetadataJson"
)
VALUES
(
    'a2186200-0000-4000-8000-000000218620',
    'PAYDAY 2',
    'steam-218620',
    'PAYDAY 2 is an action-packed, four-player co-op shooter that once again lets gamers don the masks of the original PAYDAY crew as they descend on Washington DC for an epic crime spree.',
    'Game',
    25000.00,
    true,
    now(),
    now(),
    '{
      "AppId": 218620,
      "Name": "PAYDAY 2",
      "Description": "PAYDAY 2 is an action-packed, four-player co-op shooter that once again lets gamers don the masks of the original PAYDAY crew as they descend on Washington DC for an epic crime spree.",
      "HeaderImage": "https://cdn.cloudflare.steamstatic.com/steam/apps/218620/header.jpg",
      "PosterImage": "https://cdn.cloudflare.steamstatic.com/steam/apps/218620/library_600x900.jpg",
      "Screenshots": [
        "https://cdn.cloudflare.steamstatic.com/steam/apps/218620/header.jpg"
      ],
      "Genres": ["Action", "RPG", "Co-op"],
      "Developers": ["OVERKILL - a Starbreeze Studio."],
      "Publishers": ["Starbreeze Entertainment"],
      "ReleaseDate": "13 Aug, 2013",
      "MinimumRequirements": "OS: Windows 10 (64-bit)\nProcessor: 2 GHz Intel Dual Core 64-bit processor\nMemory: 4 GB RAM\nGraphics: NVIDIA & AMD graphics card with 1 GB VRAM (DirectX 11)\nStorage: 32 GB available space",
      "RecommendedRequirements": "OS: Windows 10 (64-bit)\nProcessor: 2.3 GHz Intel Quad Core Processor\nMemory: 8 GB RAM\nGraphics: NVIDIA & AMD graphics card with 2 GB VRAM\nStorage: 32 GB available space",
      "TrailerUrl": null,
      "Source": "Steam"
    }'::jsonb
),
(
    'a4771600-0000-4000-8000-000000477160',
    'Human: Fall Flat',
    'steam-477160',
    'Human: Fall Flat is a hilarious, light-hearted physics platformer set in floating dreamscapes that can be played solo or with up to 8 players online.',
    'Game',
    35000.00,
    true,
    now(),
    now(),
    '{
      "AppId": 477160,
      "Name": "Human: Fall Flat",
      "Description": "Human: Fall Flat is a hilarious, light-hearted physics platformer set in floating dreamscapes that can be played solo or with up to 8 players online.",
      "HeaderImage": "https://cdn.cloudflare.steamstatic.com/steam/apps/477160/header.jpg",
      "PosterImage": "https://cdn.cloudflare.steamstatic.com/steam/apps/477160/library_600x900.jpg",
      "Screenshots": [
        "https://cdn.cloudflare.steamstatic.com/steam/apps/477160/header.jpg"
      ],
      "Genres": ["Adventure", "Casual", "Indie", "Simulation"],
      "Developers": ["No Brakes Games"],
      "Publishers": ["Curve Games"],
      "ReleaseDate": "22 Jul, 2016",
      "MinimumRequirements": "OS: Windows XP/Vista/7/8/8.1/10\nProcessor: Intel Core2 Duo E6750 or AMD Athlon 64 X2 6000+\nMemory: 1 GB RAM\nGraphics: GeForce GT 740 (2 GB) or Radeon HD 5770 (1 GB)\nStorage: 500 MB available space",
      "RecommendedRequirements": "OS: Windows 10/11 64-bit\nProcessor: Intel Core2 Quad Q9300 or AMD A10-5800K\nMemory: 4 GB RAM\nGraphics: GeForce GTX 460 (1 GB) or Radeon HD 7770 (1 GB)\nStorage: 500 MB available space",
      "TrailerUrl": null,
      "Source": "Steam"
    }'::jsonb
),
(
    'a2031600-0000-4000-8000-000000203160',
    'Tomb Raider',
    'steam-203160',
    'Tomb Raider explores the intense and gritty origin story of Lara Croft and her ascent from a young woman to a hardened survivor on a forgotten island.',
    'Game',
    39000.00,
    true,
    now(),
    now(),
    '{
      "AppId": 203160,
      "Name": "Tomb Raider",
      "Description": "Tomb Raider explores the intense and gritty origin story of Lara Croft and her ascent from a young woman to a hardened survivor on a forgotten island.",
      "HeaderImage": "https://cdn.cloudflare.steamstatic.com/steam/apps/203160/header.jpg",
      "PosterImage": "https://cdn.cloudflare.steamstatic.com/steam/apps/203160/library_600x900.jpg",
      "Screenshots": [
        "https://cdn.cloudflare.steamstatic.com/steam/apps/203160/header.jpg"
      ],
      "Genres": ["Action", "Adventure"],
      "Developers": ["Crystal Dynamics"],
      "Publishers": ["Crystal Dynamics"],
      "ReleaseDate": "5 Mar, 2013",
      "MinimumRequirements": "OS: Windows XP SP3 / Vista / 7 / 8\nProcessor: Dual core CPU (AMD Athlon64 X2 2.1 GHz / Intel Core2 Duo 1.86 GHz)\nMemory: 2 GB RAM\nGraphics: DirectX 9 GPU with 512 MB RAM (Radeon HD 2600 XT / GeForce 8600)\nStorage: 12 GB available space",
      "RecommendedRequirements": "OS: Windows Vista / 7 / 8 / 10\nProcessor: Quad core CPU (AMD Phenom II X4 955 / Intel Core i5-750)\nMemory: 4 GB RAM\nGraphics: DirectX 11 GPU with 1 GB RAM (Radeon HD 5870 / GeForce GTX 480)\nStorage: 12 GB available space",
      "TrailerUrl": null,
      "Source": "Steam"
    }'::jsonb
)
ON CONFLICT ("Slug") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "Description" = EXCLUDED."Description",
    "Price" = EXCLUDED."Price",
    "IsActive" = true,
    "SteamMetadataJson" = EXCLUDED."SteamMetadataJson",
    "UpdatedAtUtc" = now();

COMMIT;
