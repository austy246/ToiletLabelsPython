# Přepis ToiletLabels do .NET 10: implementační plán

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nahradit Django aplikaci funkčně shodnou ASP.NET Core 10 aplikací bez SQLite. Uživatelé budou v Azure Table Storage, lokálně poběží Docker s vlastním Azurite a nasazovat se bude z lokálního stroje přes GHCR.

**Architecture:** Jeden Razor Pages projekt `src/ToiletLabels` a jeden testovací projekt `tests/ToiletLabels.Tests`. Doménová logika je v malých třídách podle oblastí (Labels, Images, Geo, Users). Stránky jsou tenké. Storage se volá jen přes repozitáře a `IImageStore`. Image se staví ve Windows přes `docker buildx`, pushuje do GHCR a na VPS se nasazuje skriptem `deploy/deploy.ps1` přes SSH.

**Tech Stack:** .NET 10 (SDK 10.0.113), ASP.NET Core Razor Pages, Azure.Data.Tables, Azure.Storage.Blobs, Azure.Extensions.AspNetCore.DataProtection.Blobs, Microsoft.Extensions.Identity.Core (`PasswordHasher<T>`), Magick.NET-Q8-x64, xunit.v3, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.Azurite, Docker Desktop + buildx, PowerShell 7, bash na VPS.

**Spec:** `docs/superpowers/specs/2026-10-24-dotnet-rewrite-design.md`. Executor čte spec i tento plán.

## Global Constraints

- Práce probíhá přímo na `main`, bez feature větví (CLAUDE.md). Každý úkol se commitne a pushne.
- **Úkol 1 musí být pushnutý jako první.** Do té doby každý push na `main`, který mění něco mimo `docs/` a `*.md`, spustí starý workflow a nasadí Python na produkci.
- Python kód (`gallery/`, `toiletlabels/`, `manage.py`, `requirements.txt`, `tools/`, kořenový `Dockerfile`, `docker-entrypoint.sh`, `startup.sh`) se nemění ani nemaže až do úkolu 12.
- Jména storage: tabulky `ToiletLabels` a `ToiletLabelsUsers`, kontejnery `toiletlabels` a `toiletlabels-keys`. V konfiguraci jsou přepsatelná jen kvůli izolaci testů. Výchozí hodnoty se nemění.
- Konfigurační klíče: `ConnectionStrings:Storage`, `Storage:PublicBlobBaseUrl`, `Mapy:ApiKey`, `Admin:UserName`, `Admin:Password`, `AllowedHosts`. Na VPS navíc proměnná `APP_DOMAIN`.
- Entita štítku: PartitionKey `label`, RowKey = UUID s pomlčkami (`Guid.NewGuid().ToString()`). Vlastnosti `Place, Description, Country, City, MenImageUrl, WomenImageUrl, MenThumbUrl, WomenThumbUrl, NumVoters, AvgVote, Latitude, Longitude, Created`.
- Formát `Created`: `yyyy-MM-ddTHH:mm:ss.ffffff`, UTC, bez `Z`, invariant culture.
- Bloby: originál `{id}_men{ext}` / `{id}_women{ext}` (přípona z nahraného jména, včetně tečky, velikost písmen zachovat), náhled `{id}_men_thumb.webp` / `{id}_women_thumb.webp`.
- Náhled: auto-orient podle EXIF, max 400×400, bez zvětšování, WebP kvalita 80.
- Nominatim: `https://nominatim.openstreetmap.org/search?q={query}&format=json&limit=1`, User-Agent `ToiletLabels/1.0 (hausterlitz@gmail.com)`, timeout 10 s. Souřadnice se zaokrouhlí na 6 desetinných míst.
- Limit těla požadavku 50 MB (`52_428_800` bajtů).
- Texty UI se převezmou z Django šablon beze změny.
- Azurite image `mcr.microsoft.com/azure-storage/azurite:3.35.0`, spouštět s `--skipApiVersionCheck --loose`. Stejná verze lokálně i v testech.
- Image `ghcr.io/austy246/toiletlabels-app`, tag = **plné** git SHA (`git rev-parse HEAD`, stejně jako staré Python tagy) + `latest`. *(Odchylka od specu, který uvádí krátké SHA. Plné SHA sjednocuje tagy s existujícími Python image, takže `-Tag` funguje pro obě generace stejně.)*
- Shell skripty mají konce řádků LF (`.gitattributes`).
- Testy nesmí volat síť (Nominatim) ani produkční storage.

## Review Focus

1. **Produkční entity se starými nebo smíšenými typy** (`Latitude=''`, `NumVoters` jako Int64, `AvgVote` jako Double, chybějící `Country`, `City`, `*ThumbUrl`, chybějící `Created`): galerie musí vykreslit všechny a úprava nesmí změnit typ `NumVoters`/`AvgVote`. Test `LegacyEntityTests` v úkolu 3.
2. **Velká fotka z iPhonu** (rozměr 8000×6000, soubor blízko 50 MB): náhled vznikne, požadavek nad 50 MB vrátí 413, ne 500. Testy `MakeThumbnail_LargeImage_Fits400` v úkolu 4 a `Upload_OverLimit_Returns413` v úkolu 8.
3. **Server s českou kulturou** (`cs-CZ`, desetinná čárka): JSON mapy, odkaz na Mapy.com i parsování ručních souřadnic používají tečku. Test `Index_UnderCzechCulture_UsesDotDecimals` v úkolu 7.
4. **Úprava neexistujícího nebo podivného ID** (`does-not-exist`, `a'b`): admin dostane 404, ne 500. Test `Edit_UnknownId_Returns404` v úkolu 8.
5. **Otevřené přesměrování po přihlášení** (`/login/?next=https://evil.example`): po přihlášení redirect na `/`, ne ven. Test `Login_ExternalNext_RedirectsHome` v úkolu 6.

---

## Mapa souborů

```
.gitattributes                       # *.sh eol=lf (úkol 1)
.dockerignore                        # doplnit bin/obj (úkol 1)
global.json                          # SDK 10.0.113, rollForward latestFeature (úkol 1)
ToiletLabels.slnx                    # (úkol 1)
Directory.Build.props                # net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors (úkol 1)
docker-compose.yml                   # lokální: app + azurite, name: toiletlabels (úkol 10)
.env.example                         # lokální admin (úkol 10)
src/ToiletLabels/
  ToiletLabels.csproj
  Program.cs                         # argumenty --healthcheck/--check-data/--selftest, pak web host
  Dockerfile                         # build context = kořen repa (úkol 10)
  appsettings.json, appsettings.Development.json
  Cli/HealthCheckCommand.cs          # (úkol 1)
  Cli/CheckDataCommand.cs            # (úkol 9)
  Cli/SelfTestCommand.cs             # (úkol 10)
  Storage/StorageOptions.cs          # jména + PublicBlobBaseUrl (úkol 3)
  Storage/StorageBootstrapper.cs     # (úkol 3)
  Labels/Label.cs                    # (úkol 3)
  Labels/LabelEntityMapper.cs        # tolerantní čtení/zápis (úkol 3)
  Labels/CreatedTimestamp.cs         # (úkol 3)
  Labels/LabelRepository.cs          # ILabelRepository + impl (úkol 3)
  Labels/Countries.cs                # (úkol 2)
  Labels/MapPoints.cs                # (úkol 7)
  Labels/LabelEditor.cs              # create/update orchestrace (úkol 8)
  Images/ImageProcessor.cs           # Magick.NET: inspect, thumbnail, GPS (úkol 4)
  Images/BlobImageStore.cs           # IImageStore (úkol 8)
  Images/BlobNames.cs                # (úkol 8)
  Geo/GeoPoint.cs                    # (úkol 4)
  Geo/NominatimGeocoder.cs           # IGeocoder (úkol 5)
  Geo/CoordinateResolver.cs          # (úkol 5)
  Geo/ManualCoordinates.cs           # (úkol 5)
  Users/AppUser.cs, UserRepository.cs, AdminSeeder.cs, AuthSetup.cs   # (úkol 6)
  Pages/_ViewImports.cshtml, _ViewStart.cshtml, Shared/_Layout.cshtml (úkol 1)
  Pages/Index.cshtml(.cs)            # galerie (úkol 7)
  Pages/Login.cshtml(.cs), Logout.cshtml.cs   # (úkol 6)
  Pages/Upload.cshtml(.cs), Edit.cshtml(.cs)  # (úkol 8)
  Pages/Shared/_LabelForm.cshtml, _LocationPicker.cshtml, _FileUploadFix.cshtml  # (úkol 8)
tests/ToiletLabels.Tests/
  ToiletLabels.Tests.csproj
  Infrastructure/AzuriteFixture.cs   # assembly fixture, jeden kontejner na běh (úkol 1)
  Infrastructure/AppFactory.cs       # WebApplicationFactory s unikátními jmény storage (úkol 1, rozšiřuje 6)
  Infrastructure/TestImages.cs       # generování JPEG/PNG s EXIF přes Magick.NET (úkol 4)
  Fixtures/sample.heic               # (úkol 4)
  Unit/*.cs, Integration/*.cs
deploy/
  docker-compose.yml                 # produkční, .NET (úkol 11)
  toiletlabels.caddy                 # upstream :8080 (úkol 11)
  gen-env.sh, update-env-secrets.sh  # nové proměnné (úkol 11)
  migrate-env.sh                     # staré → nové proměnné, --drop-legacy (úkol 11)
  remote-deploy.sh                   # běží na VPS, posílá se přes ssh stdin (úkol 11)
  deploy.ps1                         # lokální vstupní bod (úkol 11)
  tests/test-migrate-env.sh          # (úkol 11)
```

Pořadí: 1 → (2, 3, 4, 5 nezávislé, libovolné pořadí) → 6 → 7 → 8 → 9 → 10 → 11 → 12. Úkoly 2, 4 a 5 nesahají na `Program.cs` ani na sdílené soubory a mohou běžet paralelně. Ostatní úkoly upravují `Program.cs` a běží sekvenčně.

Příkazy jsou pro PowerShell v kořeni repozitáře, pokud není uvedeno jinak.

---

### Úkol 1: Vypnutí CI a kostra řešení

**Files:**
- Delete: `.github/workflows/deploy-vps.yml`
- Create: `.gitattributes`, `global.json`, `ToiletLabels.slnx`, `Directory.Build.props`, `src/ToiletLabels/ToiletLabels.csproj`, `src/ToiletLabels/Program.cs`, `src/ToiletLabels/Cli/HealthCheckCommand.cs`, `src/ToiletLabels/appsettings.json`, `src/ToiletLabels/appsettings.Development.json`, `src/ToiletLabels/Pages/_ViewImports.cshtml`, `src/ToiletLabels/Pages/_ViewStart.cshtml`, `src/ToiletLabels/Pages/Shared/_Layout.cshtml`, `tests/ToiletLabels.Tests/ToiletLabels.Tests.csproj`, `tests/ToiletLabels.Tests/Infrastructure/AzuriteFixture.cs`, `tests/ToiletLabels.Tests/Infrastructure/AppFactory.cs`, `tests/ToiletLabels.Tests/Integration/HealthTests.cs`
- Modify: `.dockerignore` (doplnit `**/bin/`, `**/obj/`, `tests/`, `.git/`, `.vs/`, `.claude/`), `.gitignore` (doplnit `bin/`, `obj/`, `.vs/`, `*.user`), `CLAUDE.md` (sekce Deployment: push na main už nenasazuje, nasazení přes `deploy/deploy.ps1` (vznikne v úkolu 11), probíhá přepis do .NET podle specu)

**Interfaces:**
- Produces:
  - `public partial class Program` (kvůli `WebApplicationFactory<Program>`).
  - `HealthCheckCommand.RunAsync(string url = "http://localhost:8080/healthz") : Task<int>` vrací 0 při HTTP 200, jinak 1, timeout 3 s.
  - V `Program.cs` se argumenty zpracují před buildem hostu: `--healthcheck` → `HealthCheckCommand`. Další přepínače přidají úkoly 9 a 10.
  - `GET /healthz` vrací `{"status":"ok"}`.
  - `_Layout.cshtml`: port `gallery/templates/base.html`. Plovoucí tlačítko Upload vidí jen uživatel s claimem `ToiletLabels.Users.AppClaims.IsAdmin == "true"`. Konstantu `AppClaims.IsAdmin = "is_admin"` vytvoří tento úkol v `src/ToiletLabels/Users/AppClaims.cs`.
  - `AzuriteFixture`: xunit v3 assembly fixture (`[assembly: AssemblyFixture(typeof(AzuriteFixture))]`), vlastnost `string ConnectionString`. Image a argumenty podle Global Constraints.
  - `AppFactory : WebApplicationFactory<Program>`, konstruktor `(AzuriteFixture azurite)`. Nastaví `ConnectionStrings:Storage`, unikátní jména storage (`Storage:LabelsTable = "Labels" + 8 hex znaků`, obdobně `UsersTable`, `ImagesContainer = "img-" + hex`, `KeysContainer = "keys-" + hex`), `Storage:PublicBlobBaseUrl = "http://blob.test/"`, `Mapy:ApiKey = "test-key"` a prostředí `Development`. Má `ConfigureTestServices(Action<IServiceCollection>)` pro výměnu služeb (např. `IGeocoder`).

- [ ] **Step 1: Smazat workflow, commitnout a pushnout samostatně**

```powershell
git rm .github/workflows/deploy-vps.yml
git commit -m "Stop CI deploys: production will be deployed from the local machine (.NET rewrite)"
git push origin main
```
Expected: push projde. Na GitHubu v Actions nevznikne nový běh (`gh run list --limit 1` ukazuje starší běh).

- [ ] **Step 2: Napsat test `HealthTests.Healthz_ReturnsOk`**

```csharp
var client = new AppFactory(azurite).CreateClient();
var res = await client.GetAsync("/healthz");
Assert.Equal(HttpStatusCode.OK, res.StatusCode);
Assert.Equal("{\"status\":\"ok\"}", await res.Content.ReadAsStringAsync());
```
Druhý test `HealthCheckCommand_ReturnsOneWhenNothingListens`: `await HealthCheckCommand.RunAsync("http://127.0.0.1:1/healthz")` vrátí `1`.

- [ ] **Step 3: Spustit testy, ověřit selhání**

Run: `dotnet test`
Expected: chyba kompilace nebo FAIL (projekty a kód ještě neexistují).

- [ ] **Step 4: Vytvořit soubory řešení a minimální `Program.cs`**

`global.json`: `{"sdk":{"version":"10.0.113","rollForward":"latestFeature"}}`. `Directory.Build.props`: `TargetFramework net10.0`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`, `InvariantGlobalization false`. `ToiletLabels.csproj` (Sdk.Web): `ServerGarbageCollection false`, `ConcurrentGarbageCollection false`. `.gitattributes`: `* text=auto` a `*.sh text eol=lf`.

- [ ] **Step 5: Spustit testy, ověřit úspěch**

Run: `dotnet test`
Expected: 2 testy PASS. První běh stáhne Azurite image.

- [ ] **Step 6: Commit a push**

```powershell
git add -A ':!.claude'
git commit -m "Scaffold .NET 10 solution: healthz, healthcheck mode, Azurite test fixture"
git push origin main
```

---

### Úkol 2: Normalizace zemí

**Files:**
- Create: `src/ToiletLabels/Labels/Countries.cs`
- Test: `tests/ToiletLabels.Tests/Unit/CountriesTests.cs`

**Interfaces:**
- Produces: `public static class Countries` s těmito členy:
  - `IReadOnlyList<string> All` (18 kanonických jmen v pořadí z `gallery/services/countries.py`),
  - `string Normalize(string? value)`,
  - `string IsoCode(string? value)` (lowercase alpha-2 nebo `""`).

Převeď `gallery/services/countries.py` doslova: seznam, ISO mapu, aliasy i fold. Fold = `Normalize(NormalizationForm.FormKD)`, vynechat znaky kategorie `NonSpacingMark`, `ToLowerInvariant()`, `Trim()`.

- [ ] **Step 1: Napsat testy**

```csharp
[Theory]
[InlineData("cesko", "Česko")] [InlineData("Czech Republic", "Česko")] [InlineData("  ČESKÁ republika ", "Česko")]
[InlineData("uk", "Spojené království")] [InlineData("Great Britain", "Spojené království")]
[InlineData("švýcarsko", "Švýcarsko")] [InlineData("CZ", "CZ")] [InlineData("  Narnie ", "Narnie")]
[InlineData("", "")] [InlineData(null, "")]
public void Normalize_MapsToCanonical(string? input, string expected) => Assert.Equal(expected, Countries.Normalize(input));

[Fact] public void All_HasPythonOrder() { Assert.Equal(18, Countries.All.Count); Assert.Equal("Česko", Countries.All[0]); Assert.Equal("Švýcarsko", Countries.All[17]); }
[Fact] public void IsoCode_Works() { Assert.Equal("gb", Countries.IsoCode("United Kingdom")); Assert.Equal("cz", Countries.IsoCode("Česko")); Assert.Equal("", Countries.IsoCode("Narnie")); }
```

- [ ] **Step 2: Spustit a ověřit selhání**

Run: `dotnet test --filter FullyQualifiedName~CountriesTests`
Expected: FAIL (kompilace).

- [ ] **Step 3: Implementovat `Countries`**

- [ ] **Step 4: Spustit a ověřit úspěch**

Run: `dotnet test --filter FullyQualifiedName~CountriesTests`
Expected: PASS.

- [ ] **Step 5: Commit a push** (`feat: port country normalization`)

---

### Úkol 3: Model štítku, tolerantní mapování, repozitář a bootstrap storage

**Files:**
- Create: `src/ToiletLabels/Storage/StorageOptions.cs`, `src/ToiletLabels/Storage/StorageBootstrapper.cs`, `src/ToiletLabels/Labels/Label.cs`, `src/ToiletLabels/Labels/CreatedTimestamp.cs`, `src/ToiletLabels/Labels/LabelEntityMapper.cs`, `src/ToiletLabels/Labels/LabelRepository.cs`
- Modify: `src/ToiletLabels/Program.cs` (registrace `TableServiceClient`, `BlobServiceClient` z `ConnectionStrings:Storage`, options, repozitář; `await StorageBootstrapper.EnsureAsync` před `app.Run`)
- Test: `tests/ToiletLabels.Tests/Unit/LabelEntityMapperTests.cs`, `tests/ToiletLabels.Tests/Unit/CreatedTimestampTests.cs`, `tests/ToiletLabels.Tests/Integration/LabelRepositoryTests.cs`, `tests/ToiletLabels.Tests/Integration/LegacyEntityTests.cs`, `tests/ToiletLabels.Tests/Integration/StorageBootstrapperTests.cs`

**Interfaces:**
- Produces:
  - `StorageOptions` (sekce `Storage`): `LabelsTable = "ToiletLabels"`, `UsersTable = "ToiletLabelsUsers"`, `ImagesContainer = "toiletlabels"`, `KeysContainer = "toiletlabels-keys"`, `PublicBlobBaseUrl` (string, při bindování normalizovat na koncové `/`).
  - `public sealed record Label` s init vlastnostmi:
    - `required string Id`,
    - `string Place, Description, Country, City, MenImage, WomenImage, MenThumb, WomenThumb` (výchozí `""`),
    - `double? Latitude, Longitude`,
    - `int NumVoters`, `double AvgVote`,
    - `string Created` (raw, výchozí `""`),
    - computed `string Title` (Place, jinak City, jinak `"Untitled"`).
  - `CreatedTimestamp.Format(DateTime utc) : string` a `CreatedTimestamp.Now(TimeProvider) : string`.
  - `LabelEntityMapper.FromEntity(TableEntity e) : Label`. Nikdy nevyhazuje kvůli typům:
    - čísla z Int32, Int64, Double i číselných stringů,
    - `Latitude`/`Longitude` `''`, nečíselný string nebo chybějící → `null`,
    - chybějící string vlastnosti → `""`.
  - `LabelEntityMapper.ToEntity(Label label, TableEntity? existing) : TableEntity`:
    - začne kopií `existing` (nebo prázdnou entitou),
    - přepíše doménové stringy a souřadnice; `null` souřadnice vlastnost odstraní,
    - `NumVoters`, `AvgVote` a `Created` zapíše jen když `existing` je `null` (`NumVoters` jako Int32, `AvgVote` jako Double, `Created` z `label.Created`), jinak zachová původní objekty beze změny typu.
  - `ILabelRepository` s metodami:
    - `ListAsync(CancellationToken) : Task<IReadOnlyList<Label>>` (sestupně podle `Created` ordinal; prázdné `Created` na konci),
    - `GetAsync(string id, CancellationToken) : Task<Label?>` (404 i neplatný klíč → `null`),
    - `CreateAsync(Label, CancellationToken)`,
    - `UpdateAsync(Label, CancellationToken)` (načte existující entitu, `ToEntity(label, existing)`, `UpdateEntityAsync(..., TableUpdateMode.Replace)`),
    - `ListRawAsync(CancellationToken) : IAsyncEnumerable<TableEntity>`.
  - `StorageBootstrapper.EnsureAsync(bool isDevelopment, CancellationToken) : Task`:
    - vytvoří chybějící tabulky obou jmen a kontejnery `ImagesContainer` a `KeysContainer` (privátní),
    - veřejný přístup `PublicAccessType.Blob` nastaví jen když `isDevelopment` a kontejner obrázků byl právě vytvořen,
    - přístup existujícího kontejneru nikdy nemění.

- [ ] **Step 1: Napsat unit testy mapperu a timestampu**

```csharp
// CreatedTimestampTests
Assert.Equal("2026-06-03T12:34:56.123456", CreatedTimestamp.Format(new DateTime(2026,6,3,12,34,56,DateTimeKind.Utc).AddTicks(1234560)));

// LabelEntityMapperTests
[Fact] FromEntity_EmptyStringCoords_AreNull()      // Latitude="" Longitude="" → null, null
[Fact] FromEntity_NumericStringCoords_Parse()      // Latitude="50.08" → 50.08 (i pod CultureInfo("cs-CZ"))
[Fact] FromEntity_Int64Voters_DoubleAvg()          // NumVoters=3L, AvgVote=4.5 → 3, 4.5
[Fact] FromEntity_MissingOptionalProps_AreEmpty()  // jen PartitionKey/RowKey/Place → City=="", MenThumb=="", Created==""
[Fact] ToEntity_New_WritesCountersAndCreated()     // existing=null → NumVoters is int 0, AvgVote is double 0.0, Created zapsán
[Fact] ToEntity_Existing_PreservesCounterTypes()   // existing NumVoters=7L (long), AvgVote=3 (int) → po ToEntity stále long 7 a int 3; Created nezměněn
[Fact] ToEntity_NullCoords_RemovesProperties()     // existing má Latitude=50.0 → label s null → entita nemá klíč "Latitude"
```

- [ ] **Step 2: Napsat integrační testy proti Azurite**

```csharp
// LabelRepositoryTests (repo nad unikátní tabulkou z AzuriteFixture)
[Fact] List_SortsByCreatedDesc_MissingLast()   // Created "2026-01-02T..", "2026-03-01T..", "" → pořadí 2026-03, 2026-01, ""
[Fact] Get_Unknown_ReturnsNull()               // "does-not-exist" i "a'b" → null
[Fact] Update_PreservesCreatedAndVotes()

// LegacyEntityTests: přímo přes TableClient vlož
//   A: Latitude="", Longitude="", NumVoters=0 (int), AvgVote=0 (int), bez MenThumbUrl/WomenThumbUrl/Country/City
//   B: Latitude=50.08 (double), NumVoters=2L, AvgVote=4.5, bez Created
[Fact] List_ReadsAllLegacyShapes()             // ListAsync vrátí 2 položky, B poslední, A.Latitude==null
[Fact] Update_LegacyEntity_KeepsCounterTypes() // UpdateAsync(A with {Place="x"}) → NumVoters/AvgVote stále Int32

// StorageBootstrapperTests
[Fact] Ensure_CreatesAllResources_Idempotent()        // 2× EnsureAsync bez výjimky; tabulky i kontejnery existují
[Fact] Ensure_Development_MakesNewImagesContainerPublic()
[Fact] Ensure_DoesNotChangeExistingContainerAccess()  // předem vytvořený privátní images kontejner zůstane privátní i v Development
```

- [ ] **Step 3: Spustit a ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~Label|FullyQualifiedName~Created|FullyQualifiedName~Legacy|FullyQualifiedName~Bootstrapper"`
Expected: FAIL (kompilace).

- [ ] **Step 4: Implementovat soubory úkolu a registraci v `Program.cs`**

- [ ] **Step 5: Spustit a ověřit úspěch**, pak `dotnet test` celé (vše PASS)

- [ ] **Step 6: Commit a push** (`feat: label model with tolerant legacy mapping, repository, storage bootstrap`)

---

### Úkol 4: Zpracování obrázků (Magick.NET)

**Files:**
- Create: `src/ToiletLabels/Geo/GeoPoint.cs`, `src/ToiletLabels/Images/ImageProcessor.cs`, `tests/ToiletLabels.Tests/Infrastructure/TestImages.cs`, `tests/ToiletLabels.Tests/Fixtures/sample.heic`
- Modify: `src/ToiletLabels/ToiletLabels.csproj` (balíček `Magick.NET-Q8-x64`), test csproj (fixture `CopyToOutputDirectory`)
- Test: `tests/ToiletLabels.Tests/Unit/ImageProcessorTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct GeoPoint(double Latitude, double Longitude)` se statickou `GeoPoint.Rounded(double lat, double lon)` (zaokrouhlí na 6 míst, `MidpointRounding.AwayFromZero`).
  - `public sealed record ImageInfo(string ContentType, GeoPoint? Gps)`.
  - `public interface IImageProcessor`:
    - `ImageInfo? Inspect(byte[] data)`: `null`, když data nejsou čitelný obrázek. Content-Type podle formátu: `image/jpeg`, `image/png`, `image/heic`, `image/webp`, `image/gif`, jinak `application/octet-stream`.
    - `byte[]? MakeThumbnail(byte[] data)`: `null` při jakékoli chybě.
  - `ImageProcessor : IImageProcessor` (singleton). Ve statickém konstruktoru nastaví `ResourceLimits.Memory = 200 MB` (200 * 1024 * 1024).
  - GPS: z EXIF `GPSLatitude`/`GPSLatitudeRef`/`GPSLongitude`/`GPSLongitudeRef` (DMS → desetinné, S/W záporně), výsledek přes `GeoPoint.Rounded`.
  - Pro `Inspect` použít `Ping`, pokud zpřístupní EXIF profil, jinak plné čtení.
- `TestImages` (test helper):
  - `Jpeg(int w, int h, ushort? orientation = null, (double lat, double lon)? gps = null) : byte[]`,
  - `PngRgba(int w, int h) : byte[]`.
- `sample.heic`: nejdřív zkus vygenerovat HEIC přes Magick.NET (`MagickFormat.Heic` zápis) jednorázovým kódem a výsledek commitni. Pokud Magick.NET HEIC zapsat neumí, **zastav a požádej majitele** o malou fotku z iPhonu (HEIC). Nestahuj vzorky z internetu.

- [ ] **Step 1: Napsat testy**

```csharp
[Fact] MakeThumbnail_ProducesWebpWithin400()        // Jpeg(1200,800) → WebP (Magick format Webp), šířka 400, výška 267±1
[Fact] MakeThumbnail_DoesNotUpscale()               // Jpeg(200,100) → 200×100
[Fact] MakeThumbnail_HandlesRgbaPng()               // PngRgba(500,500) → ne null
[Fact] MakeThumbnail_AppliesExifOrientation()       // Jpeg(800,400, orientation:6) → výsledek 200×400 (na výšku)
[Fact] MakeThumbnail_LargeImage_Fits400()           // Jpeg(8000,6000) → 400×300
[Fact] MakeThumbnail_Garbage_ReturnsNull()          // bajty "not an image"u8 → null
[Fact] MakeThumbnail_Heic_Works()                   // Fixtures/sample.heic → ne null, max strana ≤ 400
[Fact] Inspect_Jpeg_ContentTypeAndGps()             // Jpeg(100,100, gps:(50.0875, -14.4213)) → "image/jpeg", GeoPoint(50.0875, -14.4213)
[Fact] Inspect_SouthWest_Negative()                 // gps:(-33.8688, -151.2093) → obě záporné
[Fact] Inspect_NoExif_NullGps()
[Fact] Inspect_Heic_ContentType()                   // → "image/heic"
[Fact] Inspect_Garbage_ReturnsNull()
```

- [ ] **Step 2: Spustit a ověřit selhání**

Run: `dotnet test --filter FullyQualifiedName~ImageProcessorTests`
Expected: FAIL (kompilace).

- [ ] **Step 3: Implementovat `GeoPoint`, `ImageProcessor`, `TestImages`, připravit `sample.heic`**

- [ ] **Step 4: Spustit a ověřit úspěch**

Run: `dotnet test --filter FullyQualifiedName~ImageProcessorTests`
Expected: PASS.

- [ ] **Step 5: Commit a push** (`feat: image processing with Magick.NET (thumbnails, EXIF orientation, GPS, HEIC)`)

---

### Úkol 5: Geokódování a výběr souřadnic

**Files:**
- Create: `src/ToiletLabels/Geo/NominatimGeocoder.cs`, `src/ToiletLabels/Geo/CoordinateResolver.cs`, `src/ToiletLabels/Geo/ManualCoordinates.cs`
- Test: `tests/ToiletLabels.Tests/Unit/GeocoderTests.cs`, `tests/ToiletLabels.Tests/Unit/CoordinateResolverTests.cs`, `tests/ToiletLabels.Tests/Unit/ManualCoordinatesTests.cs`

**Interfaces:**
- Consumes: `GeoPoint` (úkol 4). Pokud úkol 4 ještě není hotový, vytvoř `GeoPoint` přesně podle jeho Interfaces a úkol 4 ho převezme.
- Produces:
  - `public interface IGeocoder { Task<GeoPoint?> GeocodeAsync(string place, string city, string country, CancellationToken ct); }`.
  - `NominatimGeocoder(HttpClient http) : IGeocoder` (typed client):
    - dotaz = neprázdné části spojené `", "`; když jsou všechny prázdné, vrátí `null` bez síťového volání,
    - jakákoli chyba nebo prázdný výsledek → `null`,
    - URL, User-Agent a timeout podle Global Constraints.
  - `ManualCoordinates.Parse(string? lat, string? lon) : GeoPoint?`:
    - obě musí být neprázdné a parsovatelné (`NumberStyles.Float`, `CultureInfo.InvariantCulture`),
    - konečné, v rozsahu ±90 / ±180, jinak `null`,
    - výsledek přes `GeoPoint.Rounded`.
  - `CoordinateResolver.ResolveForCreateAsync(GeoPoint? menGps, GeoPoint? womenGps, string place, string city, string country, IGeocoder geocoder, CancellationToken ct) : Task<GeoPoint?>`: pořadí men GPS → women GPS → geocode.
  - `CoordinateResolver.ResolveForEditAsync(GeoPoint? newMenGps, GeoPoint? newWomenGps, GeoPoint? existing, string place, string city, string country, IGeocoder geocoder, CancellationToken ct) : Task<GeoPoint?>`: pořadí GPS z nově nahraných fotek → existující souřadnice → geocode. Opravuje dnešní mazání souřadnic, viz spec. *(Pořadí je rozhodnutí plánu.)*
  - Ruční souřadnice řeší volající (`LabelEditor`) před resolverem.

- [ ] **Step 1: Napsat testy**

```csharp
// GeocoderTests: HttpMessageHandler stub zachytí request
[Fact] Geocode_BuildsNominatimRequest()      // ("Hlavní nádraží","Praha","Česko") → URL q=Hlavn%C3%AD%20n%C3%A1dra%C5%BE%C3%AD%2C%20Praha%2C%20%C4%8Cesko, format=json, limit=1; UA přesně podle constraints
[Fact] Geocode_ParsesFirstResult()           // [{"lat":"50.0833331","lon":"14.4166667"}] → GeoPoint(50.083333, 14.416667)
[Fact] Geocode_AllEmpty_NoRequest()          // ("","  ","") → null, handler nevolán
[Fact] Geocode_EmptyArray_Null()
[Fact] Geocode_HttpError_Null()              // handler vyhodí HttpRequestException → null

// ManualCoordinatesTests
[Theory] [InlineData("50.0875","14.4213",true)] [InlineData("50,0875","14,4213",false)] [InlineData("","14",false)]
         [InlineData("91","0",false)] [InlineData("NaN","0",false)] [InlineData(" 50.1 "," 14.2 ",true)]
public void Parse_Cases(string lat, string lon, bool ok) { /* i pod CultureInfo.CurrentCulture = cs-CZ */ }

// CoordinateResolverTests (fake IGeocoder počítá volání)
[Fact] Create_PrefersMenGps() [Fact] Create_FallsBackToWomenThenGeocode() [Fact] Create_NothingFound_Null()
[Fact] Edit_NewPhotoWithoutGps_KeepsExisting()   // newMen=null,newWomen=null, existing=(50,14) → (50,14), geocoder nevolán
[Fact] Edit_NewPhotoGps_Overrides()
[Fact] Edit_NoExisting_Geocodes()
```

- [ ] **Step 2: Spustit a ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~Geocoder|FullyQualifiedName~Coordinate"`
Expected: FAIL (kompilace).

- [ ] **Step 3: Implementovat**

- [ ] **Step 4: Spustit a ověřit úspěch**

Run: `dotnet test --filter "FullyQualifiedName~Geocoder|FullyQualifiedName~Coordinate"`
Expected: PASS.

- [ ] **Step 5: Commit a push** (`feat: geocoding and coordinate resolution`)

---

### Úkol 6: Uživatelé, přihlášení a Data Protection

**Files:**
- Create: `src/ToiletLabels/Users/AppUser.cs`, `src/ToiletLabels/Users/UserRepository.cs`, `src/ToiletLabels/Users/AdminSeeder.cs`, `src/ToiletLabels/Users/AuthSetup.cs`, `src/ToiletLabels/Pages/Login.cshtml`, `src/ToiletLabels/Pages/Login.cshtml.cs`, `src/ToiletLabels/Pages/Logout.cshtml.cs`
- Modify: `src/ToiletLabels/Program.cs`, `src/ToiletLabels/ToiletLabels.csproj` (balíčky `Microsoft.Extensions.Identity.Core`, `Azure.Extensions.AspNetCore.DataProtection.Blobs`), `tests/ToiletLabels.Tests/Infrastructure/AppFactory.cs` (helper `Task<HttpClient> CreateAdminClientAsync()` a `CreateUserClientAsync(bool isAdmin)`, které založí uživatele a přihlásí ho přes formulář)
- Test: `tests/ToiletLabels.Tests/Integration/AuthTests.cs`, `tests/ToiletLabels.Tests/Integration/AdminSeederTests.cs`

**Interfaces:**
- Consumes: `StorageOptions.UsersTable`, `StorageOptions.KeysContainer`, `StorageBootstrapper` (úkol 3), `AppClaims.IsAdmin` (úkol 1).
- Produces:
  - `public sealed record AppUser(string UserName, string PasswordHash, bool IsAdmin, string Created)`. Entita: PartitionKey `user`, RowKey = `UserName.ToLowerInvariant()`.
  - `IUserRepository` s metodami:
    - `GetAsync(string userName, CancellationToken) : Task<AppUser?>`,
    - `TryCreateAsync(AppUser user, CancellationToken) : Task<bool>` (`false`, když už existuje).
  - `AdminSeeder.SeedAsync(CancellationToken)` (volá se po `StorageBootstrapper` při startu):
    - když jsou `Admin:UserName` i `Admin:Password` neprázdné a uživatel neexistuje, založí ho s `IsAdmin = true`,
    - existujícímu heslo nemění.
  - `AuthSetup.AddAppAuth(this IServiceCollection, IConfiguration)`:
    - Cookie auth: cookie `tl_auth`, `LoginPath = "/login"`, `ReturnUrlParameter = "next"`, `ExpireTimeSpan` 14 dní se sliding expirací, `SecurePolicy = Always` mimo Development, `HttpOnly`, `SameSite = Lax`.
    - `OnRedirectToAccessDenied` vrátí 403 bez redirectu.
    - Politika `"Admin"` vyžaduje claim `AppClaims.IsAdmin` = `"true"`.
    - Data Protection: `SetApplicationName("ToiletLabels")`, `PersistKeysToAzureBlobStorage(connectionString, KeysContainer, "keys.xml")`.
    - ForwardedHeaders: `XForwardedFor | XForwardedProto`, vyčistit `KnownIPNetworks` a `KnownProxies` (provoz jen za Caddy v Docker síti).
  - `/login`: formulář `username`, `password`, port `registration/login.html` bez odkazu „Forgot password?“.
    - Úspěch → `LocalRedirect` na `next`, pokud `Url.IsLocalUrl(next)`, jinak `/`.
    - Neúspěch → stránka znovu s chybou `"Please enter a correct username and password."` a status 200.
  - `/logout`: jen POST s antiforgery, odhlásí a přesměruje na `/`.

- [ ] **Step 1: Napsat testy**

```csharp
// AuthTests
[Fact] Login_Valid_SetsCookieAndRedirectsToNext()   // POST /login/?next=/upload/ → 302 Location /upload/
[Fact] Login_ExternalNext_RedirectsHome()           // next=https://evil.example → 302 Location "/"
[Fact] Login_WrongPassword_ShowsError()             // 200, obsahuje "Please enter a correct username and password."
[Fact] Login_PageHasNoForgotPasswordLink()          // GET /login/ neobsahuje "Forgot password"
[Fact] Logout_Post_SignsOut()                       // po POST /logout/ (s antiforgery tokenem) GET /__whoami → 401
[Fact] Logout_Get_NotAllowed()                      // GET /logout/ → 405 nebo 404; /__whoami dál vrací jméno
[Fact] AuthCookie_SurvivesNewAppInstance()          // přihlas se přes AppFactory A; nová AppFactory B se STEJNOU konfigurací storage
                                                    // (stejný KeysContainer) přijme cookie z A: GET /__whoami → 200 se jménem
// "/__whoami" existuje jen v testech: AppFactory ho přidá přes IStartupFilter. Anonymní endpoint, vrací 200 s User.Identity.Name, nebo 401.
// Stránky chráněné politikou "Admin" vzniknou až v úkolu 8; tam se testuje 302/403.

// AdminSeederTests
[Fact] Seed_CreatesAdminFromConfig()                // Admin:UserName="austy", Admin:Password="pw-1" → uživatel existuje, IsAdmin, hash ověří "pw-1"
[Fact] Seed_DoesNotOverwriteExistingPassword()      // existuje s jiným heslem → po seed se původní heslo stále ověří
[Fact] Seed_MissingPassword_DoesNothing()
[Fact] UserName_IsCaseInsensitive()                 // přihlášení "AUSTY" funguje pro "austy"
```

- [ ] **Step 2: Spustit a ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~AuthTests|FullyQualifiedName~AdminSeeder"`
Expected: FAIL (kompilace).

- [ ] **Step 3: Implementovat soubory úkolu, registraci v `Program.cs` a `UseForwardedHeaders` jako první middleware**

- [ ] **Step 4: Spustit a ověřit úspěch**, pak `dotnet test` celé (vše PASS)

- [ ] **Step 5: Commit a push** (`feat: users in Table Storage, cookie auth, blob-persisted data protection`)

---

### Úkol 7: Galerie a mapa (`/`)

**Files:**
- Create: `src/ToiletLabels/Labels/MapPoints.cs`, `src/ToiletLabels/Pages/Index.cshtml`, `src/ToiletLabels/Pages/Index.cshtml.cs`
- Modify: `src/ToiletLabels/Program.cs` (options `Mapy`)
- Test: `tests/ToiletLabels.Tests/Unit/MapPointsTests.cs`, `tests/ToiletLabels.Tests/Integration/GalleryPageTests.cs`

**Interfaces:**
- Consumes: `ILabelRepository.ListAsync`, `Label`, `StorageOptions.PublicBlobBaseUrl` (úkol 3), `Countries.IsoCode` (úkol 2), `AppClaims.IsAdmin`.
- Produces:
  - `public sealed record MapPoint(double Lat, double Lon, string Place, string MenUrl, string WomenUrl, string RowKey)`. Jména v JSON jsou přes `[JsonPropertyName]` přesně `lat`, `lon`, `place`, `men_url`, `women_url`, `row_key`, jako v Pythonu, protože je čte převzaté JS.
  - `MapPoints.Build(IEnumerable<Label> labels, string blobBaseUrl) : IReadOnlyList<MapPoint>`:
    - jen štítky s oběma souřadnicemi,
    - URL = thumb, jinak originál, s prefixem `blobBaseUrl`; prázdné jméno → `""`,
    - `Place` = `Label.Title`.
  - Stránka `Index` je port `gallery/templates/gallery/signpair_list.html` (bez mrtvého bloku stránkování) s těmito pravidly:
    - data mapy v `<script type="application/json" id="map-points-data">` přes `System.Text.Json` (escapuje `<`, `>`, `&`),
    - mapa se vykreslí jen při neprázdných bodech a `Mapy:ApiKey`,
    - karta má `id="pair-{RowKey}"`,
    - vlajka `https://flagcdn.com/24x18/{iso}.png` (+ `48x36` v srcset),
    - odkaz `https://mapy.com/fnc/v1/showmap?center={lon},{lat}&zoom=17&marker=true` s invariantními čísly,
    - datum = prvních 10 znaků `Created`,
    - tlačítko Edit jen pro admina.

- [ ] **Step 1: Napsat testy**

```csharp
// MapPointsTests
[Fact] Build_OnlyLabelsWithCoords()
[Fact] Build_PlaceFallsBackToCityThenUntitled()
[Fact] Build_PrefersThumbnail()                 // MenThumb="a_men_thumb.webp" → "http://blob.test/a_men_thumb.webp"; bez thumbu → originál

// GalleryPageTests (vlož entity přes repozitář)
[Fact] Index_RendersMapWhenPointsAndKey()       // obsahuje id="map-points-data", "api.mapy.com/v1/maptiles/basic/256", id="pair-id1", viewBox="0 0 448 512"
[Fact] Index_HidesMapWithoutKey()               // Mapy:ApiKey="" → neobsahuje map-points-data ani "api.mapy.com"
[Fact] Index_CardUsesThumbnailWithFallback()
[Fact] Index_ViewOnMapLinksToMapy()             // obsahuje "mapy.com/fnc/v1/showmap?center=14.42,50.08", neobsahuje "google"
[Fact] Index_UnderCzechCulture_UsesDotDecimals()// CultureInfo.DefaultThreadCurrentCulture = cs-CZ → "center=14.42,50.08" a "\"lat\":50.08"
[Fact] Index_ShowsFlagForKnownCountry()         // Country="Česko" → "flagcdn.com/24x18/cz.png"
[Fact] Index_EscapesPlaceInJson()               // Place="</script><b>" → raw "</script><b>" se v HTML nevyskytuje
[Fact] Index_EmptyState()                       // žádné štítky → "No signs yet"
[Fact] Index_EditButtonOnlyForAdmin()           // anonym: neobsahuje "/pair/id1/edit/"; admin: obsahuje
[Fact] Index_LegacyEntitiesRender()             // entity A a B z LegacyEntityTests → 200, oba pair-* id přítomny
```

- [ ] **Step 2: Spustit a ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~MapPoints|FullyQualifiedName~GalleryPage"`
Expected: FAIL.

- [ ] **Step 3: Implementovat `MapPoints` a stránku `Index`**

- [ ] **Step 4: Spustit a ověřit úspěch**

Run: `dotnet test --filter "FullyQualifiedName~MapPoints|FullyQualifiedName~GalleryPage"`
Expected: PASS.

- [ ] **Step 5: Commit a push** (`feat: gallery page with map`)

---

### Úkol 8: Nahrávání a úprava (`/upload/`, `/pair/{id}/edit/`)

**Files:**
- Create: `src/ToiletLabels/Images/BlobNames.cs`, `src/ToiletLabels/Images/BlobImageStore.cs`, `src/ToiletLabels/Labels/LabelEditor.cs`, `src/ToiletLabels/Pages/Upload.cshtml(.cs)`, `src/ToiletLabels/Pages/Edit.cshtml(.cs)`, `src/ToiletLabels/Pages/Shared/_LabelForm.cshtml`, `src/ToiletLabels/Pages/Shared/_LocationPicker.cshtml`, `src/ToiletLabels/Pages/Shared/_FileUploadFix.cshtml`
- Modify: `src/ToiletLabels/Program.cs` (služby; Kestrel `MaxRequestBodySize` a `FormOptions.MultipartBodyLengthLimit` = 52_428_800; `HttpClient` pro `NominatimGeocoder`)
- Test: `tests/ToiletLabels.Tests/Unit/BlobNamesTests.cs`, `tests/ToiletLabels.Tests/Integration/UploadTests.cs`, `tests/ToiletLabels.Tests/Integration/EditTests.cs`

**Interfaces:**
- Consumes: `ILabelRepository`, `Label`, `CreatedTimestamp`, `StorageOptions` (úkol 3); `IImageProcessor`, `ImageInfo` (úkol 4); `IGeocoder`, `CoordinateResolver`, `ManualCoordinates` (úkol 5); politika `"Admin"` (úkol 6); `Countries` (úkol 2).
- Produces:
  - `BlobNames.Original(string id, string side, string fileName) : string` (`side` je `"men"`/`"women"`, přípona přes `Path.GetExtension`).
  - `BlobNames.Thumb(string originalName) : string` (`{bez přípony}_thumb.webp`).
  - `IImageStore` s metodami:
    - `UploadAsync(string name, byte[] data, string contentType, CancellationToken)` (overwrite),
    - `DeleteIfExistsAsync(string name, CancellationToken)`,
    - `PublicUrl(string name) : string` (`""` pro prázdné jméno).
  - `public sealed record LabelInput(string Place, string Description, string Country, string City, string? Latitude, string? Longitude)`.
  - `public sealed record UploadedImage(string FileName, byte[] Data)`.
  - `LabelEditor.CreateAsync(LabelInput input, UploadedImage men, UploadedImage women, CancellationToken) : Task<string>` (vrací id):
    - `Country` přes `Countries.Normalize`,
    - souřadnice: ruční → `ResolveForCreateAsync`,
    - nahraje originály s `ImageInfo.ContentType`, pak náhledy (`image/webp`); `MakeThumbnail == null` → prázdné jméno náhledu,
    - `CreateAsync` s `Created = CreatedTimestamp.Now`.
  - `LabelEditor.UpdateAsync(string id, LabelInput input, UploadedImage? men, UploadedImage? women, CancellationToken) : Task<bool>` (`false` = neexistuje):
    - souřadnice: ruční → `ResolveForEditAsync`,
    - nový originál s jiným jménem než starý → po úspěšném nahrání smaže starý originál; starý náhled se přepíše, protože má stejné jméno.
  - Validace stránek (chyby ve formuláři, status 200, nic se nezapíše):
    - chybějící fotka při uploadu: `"Nahraj fotku – muži."` / `"Nahraj fotku – ženy."`,
    - soubor, který `Inspect` nepřečte: `"Soubor není obrázek."`,
    - `Place` delší než 200 znaků nebo `Description` delší než 2000: `"Text je příliš dlouhý."`.
    - *(Texty a limity jsou rozhodnutí plánu.)*
  - Stránky jsou porty `upload_label.html`, `edit_label.html`, `_location_picker.html` a `_file_upload_fix.html`:
    - texty, pořadí polí (fotky první) a JS beze změny,
    - exifr pinnout na `https://cdn.jsdelivr.net/npm/exifr@7.1.3/dist/full.umd.js`,
    - neznámá legacy země se v selectu na editu zobrazí jako vybraná extra volba.
  - Routy:
    - `@page "/upload"` a `@page "/pair/{id}/edit"`, kde URL s koncovým lomítkem musí fungovat (`/upload/`, `/pair/x/edit/`),
    - obě vyžadují politiku `"Admin"`,
    - po úspěchu 302 na `/`,
    - neznámé id → 404.

- [ ] **Step 1: Napsat testy**

```csharp
// BlobNamesTests
Assert.Equal("id1_men.JPG", BlobNames.Original("id1","men","IMG_1.JPG"));
Assert.Equal("id1_men_thumb.webp", BlobNames.Thumb("id1_men.JPG"));

// UploadTests (fake IGeocoder; obsah blob kontejneru čti přes BlobContainerClient)
[Fact] Upload_Anonymous_RedirectsToLogin()        // GET /upload/ → 302, Location začíná "/login" a obsahuje "next=%2Fupload%2F"
[Fact] Upload_NonAdmin_Forbidden()                // 403
[Fact] Upload_Get_RendersFormImagesFirst()        // index "men_image" < index "name=\"place\""; obsahuje id="location-map", "exifr@7.1.3"
[Fact] Upload_StoresOriginalsThumbsAndEntity()    // Jpeg s GPS → 4 bloby {id}_men.jpg (Content-Type image/jpeg), {id}_men_thumb.webp (image/webp)...; entita má Latitude z EXIF; 302 na /
[Fact] Upload_ManualCoordsWin()                   // latitude=49.1, longitude=16.6 + EXIF jiné → 49.1/16.6, geocoder nevolán
[Fact] Upload_NormalizesCountry()                 // country="czech republic" → "Česko"
[Fact] Upload_MissingWomenImage_ShowsError()      // 200, "Nahraj fotku – ženy.", tabulka prázdná, kontejner prázdný
[Fact] Upload_NotAnImage_ShowsError()             // "Soubor není obrázek."
[Fact] Upload_Heic_Works()                        // sample.heic jako men → originál Content-Type image/heic, náhled existuje
[Fact] Upload_OverLimit_Returns413()              // multipart > 52_428_800 B → 413

// EditTests
[Fact] Edit_UnknownId_Returns404()                // admin GET /pair/does-not-exist/edit/ i /pair/a'b/edit/ → 404
[Fact] Edit_PreservesCreatedAndVotes()
[Fact] Edit_ManualCoordsWin()
[Fact] Edit_NewPhotoWithoutGps_KeepsCoords()      // existující 50/14, nová fotka bez EXIF, bez ručních → 50/14
[Fact] Edit_ChangedExtension_DeletesOldOriginal() // původně id_men.jpg, nově .png → id_men.jpg neexistuje, id_men.png existuje, MenImageUrl == "id_men.png"
[Fact] Edit_NoNewFiles_KeepsImageNames()
[Fact] Edit_Get_ShowsLegacyCountryOption()        // Country="Narnie" → <option ... selected>Narnie</option>
```

- [ ] **Step 2: Spustit a ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~BlobNames|FullyQualifiedName~UploadTests|FullyQualifiedName~EditTests"`
Expected: FAIL.

- [ ] **Step 3: Implementovat**

- [ ] **Step 4: Spustit a ověřit úspěch**, pak `dotnet test` celé (vše PASS)

- [ ] **Step 5: Commit a push** (`feat: upload and edit pages`)

---

### Úkol 9: Režim `--check-data`

**Files:**
- Create: `src/ToiletLabels/Cli/CheckDataCommand.cs`
- Modify: `src/ToiletLabels/Program.cs` (přepínač `--check-data` před buildem web hostu)
- Test: `tests/ToiletLabels.Tests/Integration/CheckDataTests.cs`

**Interfaces:**
- Consumes: `LabelEntityMapper.FromEntity` (úkol 3), `StorageOptions`.
- Produces: `CheckDataCommand.RunAsync(IConfiguration config, TextWriter output, CancellationToken) : Task<int>`. Chování:
  - Postaví vlastní `TableServiceClient`, **nevolá `StorageBootstrapper` ani nic nezapisuje**. Když tabulka štítků neexistuje, vypíše `"Table ToiletLabels not found"` a vrátí 1.
  - Každou entitu převede přes `FromEntity` v try/catch.
  - Vypíše:
    - `Labels: {n}, unreadable: {k}`,
    - počty bez souřadnic, bez náhledů a bez `Created`,
    - seznam nalezených typů `NumVoters`/`AvgVote`,
    - pro každou nečitelnou entitu `RowKey: chyba`.
  - Návratový kód je 0, když `k == 0`, jinak 1.

- [ ] **Step 1: Napsat testy**

```csharp
[Fact] CheckData_ReportsCountsAndTypes()        // entity A, B z LegacyEntityTests → exit 0; výstup obsahuje "Labels: 2, unreadable: 0", "Int32", "Int64"
[Fact] CheckData_MissingTable_FailsWithoutCreating() // neexistující tabulka → exit 1 a tabulka po běhu stále neexistuje
[Fact] CheckData_DoesNotCreateUsersTableOrKeys()     // po běhu neexistuje UsersTable ani KeysContainer
```

- [ ] **Step 2: Spustit a ověřit selhání**

Run: `dotnet test --filter FullyQualifiedName~CheckDataTests`
Expected: FAIL.

- [ ] **Step 3: Implementovat**

- [ ] **Step 4: Spustit a ověřit úspěch**

Run: `dotnet test --filter FullyQualifiedName~CheckDataTests`
Expected: PASS.

- [ ] **Step 5: Commit a push** (`feat: read-only --check-data mode`)

---

### Úkol 10: Docker image a lokální prostředí

**Files:**
- Create: `src/ToiletLabels/Dockerfile`, `src/ToiletLabels/Cli/SelfTestCommand.cs`, `docker-compose.yml`, `.env.example`
- Modify: `src/ToiletLabels/Program.cs` (přepínač `--selftest`), `README.md` (sekce „Lokální vývoj (.NET)“: `docker compose up -d --build`, aplikace na `http://localhost:8000`, Azurite bloby na `10100`, tabulky na `10102`, `dotnet test`)
- Test: `tests/ToiletLabels.Tests/Unit/SelfTestTests.cs`

**Interfaces:**
- Produces:
  - `SelfTestCommand.Run(TextWriter output) : int` ověří tři věci:
    1. Magick.NET se načte a `MagickNET.SupportedFormats` obsahuje HEIC se čtením.
    2. WebP zápis vygenerovaného obrázku 10×10 se povede.
    3. `Countries.Normalize("ČESKÁ republika") == "Česko"`.
    Každou kontrolu vypíše jako `OK …` nebo `FAIL …`. Vrátí 0 jen když projdou všechny.
  - Dockerfile:
    - build stage `mcr.microsoft.com/dotnet/sdk:10.0`: restore a publish `-r linux-x64 --no-self-contained`, BuildKit cache mount na `/root/.nuget/packages`,
    - runtime `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`,
    - `ENTRYPOINT ["dotnet", "ToiletLabels.dll"]`, port 8080,
    - build context je kořen repa: `docker build -f src/ToiletLabels/Dockerfile .`.
  - `docker-compose.yml` (lokální, `name: toiletlabels`):
    - služba `app`:
      - build podle Dockerfile výše, port `8000:8080`, `ASPNETCORE_ENVIRONMENT=Development`,
      - `ConnectionStrings__Storage` s Azurite well-known účtem `devstoreaccount1` a endpointy `http://azurite:10000/devstoreaccount1` a `http://azurite:10002/devstoreaccount1`,
      - `Storage__PublicBlobBaseUrl=http://localhost:10100/devstoreaccount1/toiletlabels/`,
      - `Mapy__ApiKey`, `Admin__UserName` a `Admin__Password` z `.env`,
      - healthcheck přes `--healthcheck`, `depends_on: azurite`;
    - služba `azurite`: image podle Global Constraints, `azurite --blobHost 0.0.0.0 --tableHost 0.0.0.0 --skipApiVersionCheck --loose --location /data`, porty `10100:10000` a `10102:10002`, volume `azurite-data:/data`.

- [ ] **Step 1: Napsat test `SelfTest_PassesOnDevMachine`** (`SelfTestCommand.Run(writer) == 0`, výstup obsahuje tři řádky `OK`)

- [ ] **Step 2: Spustit a ověřit selhání, implementovat, ověřit úspěch**

Run: `dotnet test --filter FullyQualifiedName~SelfTestTests`
Expected: nejdřív FAIL, po implementaci PASS.

- [ ] **Step 3: Postavit image a spustit selftest v chiseled runtime**

```powershell
docker build -f src/ToiletLabels/Dockerfile -t toiletlabels-app:local .
docker run --rm toiletlabels-app:local --selftest
```
Expected: tři řádky `OK` a exit 0.
- Když selže HEIC nebo Magick: zkus runtime `10.0-noble-chiseled-extra`.
- Když selže fold zemí: použij `-extra` (ICU).
- Když selže obojí: použij `10.0-noble`.
Volbu a důvod zapiš jako komentář nad `FROM` v Dockerfile.

- [ ] **Step 4: Spustit lokální prostředí**

```powershell
Copy-Item .env.example .env   # lokální admin heslo vygeneruje controller (testovací hodnota, .env je gitignored); Mapy klíč doplní majitel, bez něj se mapa skryje
docker compose up -d --build --wait
docker compose ps
Invoke-RestMethod http://localhost:8000/healthz
```
Expected:
- kontejnery `toiletlabels-app-1` (healthy) a `toiletlabels-azurite-1`, volume `toiletlabels_azurite-data`,
- healthz vrátí `status = ok`.

- [ ] **Step 5: Ruční smoke test v prohlížeči (provádí controller, ne implementer)**

Na `http://localhost:8000` projít:
1. Přihlásit se.
2. Nahrát pár JPEG + HEIC. Náhledy se zobrazí, obrázky se načítají z `localhost:10100`.
3. Upravit pár a změnit fotku na PNG.
4. `docker compose restart app` → admin zůstane přihlášený.
5. Odhlásit se.

- [ ] **Step 6: Změřit a zapsat do README**

- velikost image: `docker image ls toiletlabels-app:local`,
- paměť po smoke testu: `docker stats --no-stream toiletlabels-app-1`.

Porovnej s Python image (`docker image ls` po `docker pull ghcr.io/austy246/toiletlabels-app:5a76bb39ff751ca37a34d6b0771c5bc9bda143dd`). Pokud paměť přesáhne 400 MB, zastav a nahlas to.

- [ ] **Step 7: Commit a push** (`feat: chiseled Docker image, selftest, local compose with Azurite`)

---

### Úkol 11: Nasazení z lokálního stroje

**Files:**
- Create: `deploy/migrate-env.sh`, `deploy/remote-deploy.sh`, `deploy/deploy.ps1`, `deploy/tests/test-migrate-env.sh`
- Modify: `deploy/docker-compose.yml`, `deploy/toiletlabels.caddy` (jen `reverse_proxy toiletlabels-app:8080`), `deploy/gen-env.sh`, `deploy/update-env-secrets.sh`, `.gitignore` (`deploy/secrets.env`)

**Interfaces:**
- Consumes: image a přepínače `--healthcheck`, `--selftest` a `--check-data` (úkoly 1, 9, 10). Původní logika nasazení: `git show 5a76bb39ff751ca37a34d6b0771c5bc9bda143dd:.github/workflows/deploy-vps.yml`.
- Produces:
  - `deploy/docker-compose.yml`:
    - jako dnes, ale `image: ${APP_IMAGE:?APP_IMAGE missing}`, bez `build`, bez volume, healthcheck `["CMD", "dotnet", "ToiletLabels.dll", "--healthcheck"]` (interval 30 s, timeout 5 s, start_period 20 s, retries 3),
    - zachovat `mem_limit: 512m`, `cpus: 1`, `cpu_shares: 256`, `oom_score_adj: 500` (včetně komentáře), logging, síť `web` s aliasem `toiletlabels-app`.
  - `migrate-env.sh [--drop-legacy] [ENV_FILE=deploy/.env]`, idempotentní, `umask 077`, záloha `.env.bak-YYYYmmddHHMMSS`, nikdy nevypisuje hodnoty:
    - doplní chybějící nové klíče:
      - `ConnectionStrings__Storage` ← `AZURE_STORAGE_CONNECTION_STRING`,
      - `Admin__UserName` ← `DJANGO_SUPERUSER_USERNAME` (výchozí `austy`),
      - `Admin__Password` ← `DJANGO_SUPERUSER_PASSWORD`,
      - `Mapy__ApiKey` ← `MAPY_API_KEY`,
      - `APP_DOMAIN` a `AllowedHosts` ← první položka `DJANGO_ALLOWED_HOSTS`,
      - `Storage__PublicBlobBaseUrl` = `https://{AccountName z connection stringu}.blob.core.windows.net/toiletlabels/`,
      - `ASPNETCORE_ENVIRONMENT=Production`;
    - existující nové klíče nepřepisuje;
    - `--drop-legacy` odstraní `AZURE_STORAGE_CONNECTION_STRING`, `MAPY_API_KEY`, `DJANGO_*`, `CSRF_TRUSTED_ORIGINS`.
  - `update-env-secrets.sh`: allow-list `ConnectionStrings__Storage`, `Mapy__ApiKey`, `Admin__UserName`, `Admin__Password`, zbytek chování beze změny.
  - `gen-env.sh`: pro čistý server zapisuje nové klíče (`COMPOSE_PROJECT_NAME=toiletlabels`, `APP_DOMAIN`, `AllowedHosts`, `ASPNETCORE_ENVIRONMENT`, `Storage__PublicBlobBaseUrl` pro `toiletlabelsstorage`, `APP_IMAGE=`).
  - `remote-deploy.sh IMAGE_REF [--selftest] [--prepare-only]`, běží na VPS přes `ssh … bash -s -- args < remote-deploy.sh` v `/srv/toiletlabels/deploy`, `set -euo pipefail`:
    1. Ověří, že existuje `.env`.
    2. `docker pull IMAGE_REF` anonymně.
    3. S `--selftest` spustí `docker run --rm IMAGE_REF --selftest` (fail → exit 1).
    4. S `--prepare-only` spustí `./migrate-env.sh`, pak `docker run --rm --env-file .env IMAGE_REF --check-data` a skončí.
    5. Jinak nastaví `APP_IMAGE=IMAGE_REF` v `.env`, `docker compose pull app` a `docker compose up -d --wait --remove-orphans`.
    6. Vyrenderuje Caddy jako ve starém workflow, ale doménu bere z `APP_DOMAIN`, s fallbackem na první položku `DJANGO_ALLOWED_HOSTS` kvůli rollbacku na Python. Validate, reload a rollback jako dřív.
    7. Smaže jen staré tagy `ghcr.io/austy246/toiletlabels-app` kromě aktuálního a předchozího. **Nikdy ne `docker image prune -af`**, protože VPS sdílí další projekty.
  - `deploy.ps1` s parametry:
    - `[string]$Tag`,
    - `[switch]$PrepareOnly`, `[switch]$UpdateSecrets`, `[switch]$DryRun`,
    - `[string]$SshTarget = "deploy@57.131.165.173"`, `[string]$SshKey = "$HOME/.ssh/id_rsa_austy"`, `[string]$Domain = "toiletlabels.austy.cz"`.

    Postup:
    1. Bez `-Tag`:
       - tracked změny mimo `.claude/` → chyba,
       - `$Tag = git rev-parse HEAD`,
       - `dotnet test` (fail → konec),
       - `docker buildx build --platform linux/amd64 -f src/ToiletLabels/Dockerfile -t img:$Tag -t img:latest --push .`
    2. S `-Tag`: ověří existenci přes `docker manifest inspect img:$Tag`, nic nestaví.
    3. Nahraje deploy soubory z commitu: `cmd /c "git archive --format=tar $Tag deploy | ssh -i $SshKey $SshTarget tar -x -C /srv/toiletlabels"`.
    4. `-UpdateSecrets`: `cmd /c "ssh … bash /srv/toiletlabels/deploy/update-env-secrets.sh < deploy\secrets.env"`.
    5. Spustí `remote-deploy.sh` z **aktuální** pracovní kopie přes `cmd /c "ssh … bash -s -- img:$Tag [flags] < deploy\remote-deploy.sh"`. `--selftest` přidá, když `git cat-file -e "${Tag}:src/ToiletLabels/ToiletLabels.csproj"` uspěje; `-PrepareOnly` → `--prepare-only`.
    6. Bez `-PrepareOnly` ověří `https://$Domain/healthz` (5 pokusů po 3 s).

    `-DryRun` jen vypíše příkazy.

- [ ] **Step 1: Napsat test `deploy/tests/test-migrate-env.sh`** (bash, spouštěný přes Git Bash)

Testovací `.env` s `AZURE_STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=https;AccountName=toiletlabelsstorage;AccountKey=abc==;EndpointSuffix=core.windows.net`, `DJANGO_ALLOWED_HOSTS=toiletlabels.austy.cz,localhost`, `DJANGO_SUPERUSER_PASSWORD=pw`, `MAPY_API_KEY=k`, `APP_IMAGE=x`. Assert:
- po migraci obsahuje `APP_DOMAIN=toiletlabels.austy.cz`, `AllowedHosts=toiletlabels.austy.cz`, `Storage__PublicBlobBaseUrl=https://toiletlabelsstorage.blob.core.windows.net/toiletlabels/`, `Admin__UserName=austy`, `Admin__Password=pw` a connection string beze změny (včetně `==`);
- druhé spuštění soubor nezmění (porovnat `sha256sum`);
- stdout neobsahuje `abc==` ani `pw`;
- `--drop-legacy` odstraní `DJANGO_*`, `AZURE_STORAGE_CONNECTION_STRING` a `MAPY_API_KEY` a ponechá `APP_IMAGE=x`.

- [ ] **Step 2: Spustit a ověřit selhání**

Run (Bash tool / Git Bash): `bash deploy/tests/test-migrate-env.sh`
Expected: FAIL (skript neexistuje).

- [ ] **Step 3: Implementovat skripty a compose, pak ověřit**

Run:
- `bash deploy/tests/test-migrate-env.sh` → PASS,
- `bash -n deploy/remote-deploy.sh deploy/migrate-env.sh deploy/gen-env.sh deploy/update-env-secrets.sh` → bez chyb,
- `docker compose -f deploy/docker-compose.yml --env-file NUL config` s `APP_IMAGE=x` v prostředí → validní,
- `pwsh -NoProfile -Command "./deploy/deploy.ps1 -DryRun"` → vypíše kroky v pořadí test → buildx → git archive/ssh → remote-deploy → healthz,
- `pwsh -NoProfile -Command "./deploy/deploy.ps1 -DryRun -Tag 5a76bb39ff751ca37a34d6b0771c5bc9bda143dd"` → bez testů a buildu, bez `--selftest`.

- [ ] **Step 4: Commit a push** (`feat: deploy from local machine via GHCR and SSH`)

---

### Úkol 12: Cutover na VPS (provádí controller se zastávkami) a úklid

Úkol neplní implementer subagent. Provádí ho controller session podle runbooku ve specu. **Zastávky pro souhlas majitele jsou povinné.** Agent nezadává hesla ani tokeny.

**Files:**
- Delete (krok 7): `gallery/`, `toiletlabels/`, `manage.py`, `requirements.txt`, `tools/`, `Dockerfile`, `docker-entrypoint.sh`, `startup.sh`, `.vscode/launch.json` (pokud spouští Django)
- Modify (krok 7): `CLAUDE.md`, `docs/provoz-vps.md`, `README.md`, `.gitignore` (odstranit Python sekce), `.dockerignore`

- [ ] **Step 1: Majitel:** `docker login ghcr.io` s classic PAT (`write:packages`). Controller pak ověří, že GHCR balíček je veřejný: na VPS `ssh … docker manifest inspect ghcr.io/austy246/toiletlabels-app:5a76bb39ff751ca37a34d6b0771c5bc9bda143dd` uspěje bez přihlášení. Pokud ne, majitel přepne viditelnost balíčku na Public.

- [ ] **Step 2: Příprava bez přepnutí**

Run: `./deploy/deploy.ps1 -PrepareOnly`
Expected:
- testy PASS, image pushnutý, selftest `OK` ×3,
- `migrate-env.sh` doplní klíče,
- `--check-data` vypíše `unreadable: 0` a exit 0.

Při chybě stop. Výstup `--check-data` ukaž majiteli.

- [ ] **Step 3: STOP. Zeptat se majitele na souhlas s nasazením** (uvést tag a výsledek `--check-data`)

- [ ] **Step 4: Nasazení**

Run: `./deploy/deploy.ps1 -Tag <sha z kroku 2>`
Expected: `up --wait` healthy, Caddy reload OK, `https://toiletlabels.austy.cz/healthz` vrátí ok.

- [ ] **Step 5: Ověření**

1. Controller: galerie vykreslí stejný počet karet jako `Labels:` z `--check-data` a obrázky se načítají.
2. Majitel: přihlásí se (heslo zná jen on).
3. Controller: `ssh … "cd /srv/toiletlabels/deploy && docker compose restart app"`.
4. Majitel: obnoví stránku a potvrdí, že je stále přihlášený.
5. Volitelně se souhlasem majitele: upload a úprava testovacího páru, který majitel pak smaže v Azure.

Selhání → rollback: `./deploy/deploy.ps1 -Tag 5a76bb39ff751ca37a34d6b0771c5bc9bda143dd` a nahlásit.

- [ ] **Step 6: STOP. Zeptat se majitele na souhlas se smazáním volume a starých proměnných.** Pak:

```powershell
ssh -i $HOME/.ssh/id_rsa_austy deploy@57.131.165.173 "cd /srv/toiletlabels/deploy && ./migrate-env.sh --drop-legacy && docker volume rm toiletlabels_app_data"
```
Expected: volume smazán. Aplikace běží dál (`docker compose ps` healthy), protože volume nepoužívá.

- [ ] **Step 7: Úklid repozitáře**

1. Smazat Python soubory (viz Files).
2. Aktualizovat `CLAUDE.md`: .NET příkazy, `dotnet test`, nasazení `deploy/deploy.ps1`, lokální `docker compose`, nové proměnné, Users table, bez SQLite.
3. Aktualizovat `docs/provoz-vps.md`: nasazení, rollback `-Tag`, `--check-data`, `--selftest`, `.env` klíče, Data Protection kontejner.
4. Aktualizovat `README.md`.
5. Spustit `dotnet test` (PASS).
6. Commit a push (`chore: remove Django app after .NET cutover; update docs`).
