# VELO BROWSER — DOCUMENTACIÓN TÉCNICA ULTRA-DETALLADA v2.2
> Documento maestro para implementación. Generado para ser consumido directamente por cualquier IA o desarrollador que vaya a escribir código C#.
> Sin resúmenes ejecutivos. Sin ambigüedades. Todo lo que no está aquí, no existe en el proyecto.

---

## ÍNDICE

1. Información General del Proyecto
2. Stack Tecnológico Completo
3. Estructura de Repositorio
4. Arquitectura de Capas — Orden de Ejecución Real
5. Capa 1 — UI (WPF)
6. Capa 2 — Cookie Wall Bypass Engine
7. Capa 3 — Security Guards
8. Capa 4 — AI Security Engine (Pluggable)
9. Capa 5 — DNS Privado (DoH Engine)
10. Capa 6 — WebView2 Engine
11. Módulos Transversales
12. Flujos Críticos Detallados
13. Modelos de Datos (SQLite)
14. Configuración y Settings
15. Onboarding Wizard
16. Roadmap por Fases
17. Decisiones de Diseño Inamovibles
18. Notas de Implementación por Módulo

---

## 1. INFORMACIÓN GENERAL DEL PROYECTO

```
Nombre:             VELO
Versión:            2.0
Plataforma:         Windows 10 / Windows 11 (64-bit únicamente)
Lenguaje:           C# 12 / .NET 8
Licencia:           AGPLv3
Tamaño instalador:  ≤ 25 MB (WebView2 ya está en el sistema)
Repositorio:        GitHub (público, open-source)
```

**Filosofía — inamovible:**
- Zero telemetría hardcodeada. No hay opción para activarla.
- IA es opcional. El navegador funciona 100% sin ninguna API key.
- Sin extensiones. Todo lo que normalmente haría una extensión está integrado nativamente.
- Sin bloat. Si una feature no mejora privacidad, seguridad o UX directamente, no existe.
- Todo auditable. Código abierto, AGPLv3 obliga a cualquier fork a serlo también.

**Público objetivo:**
Usuarios que buscan privacidad real sin configurar nada, desarrolladores, periodistas, investigadores de seguridad, y cualquiera que considere que Chrome, Edge, Firefox y Brave tienen demasiado que no pidieron.

---

## 2. STACK TECNOLÓGICO COMPLETO

### 2.1 Core

| Componente | Tecnología | Versión | Justificación |
|---|---|---|---|
| Lenguaje | C# | 12 | Tipado fuerte, ecosistema Windows nativo |
| Runtime | .NET | 8 LTS | Soporte hasta 2026, rendimiento nativo |
| UI Framework | WPF | .NET 8 | Nativo Windows, sin Electron, sin 150MB extra |
| Motor Web | Microsoft.Web.WebView2 | Latest stable | Chromium reutilizado del sistema, actualizaciones automáticas por Microsoft |
| Base de datos | SQLite + SQLCipher | Latest | Encriptación AES-256 nativa, sin servidor |
| ORM | sqlite-net-pcl | Latest | Ligero, compatible con SQLCipher |

### 2.2 Seguridad y Cifrado

| Componente | Tecnología | Notas |
|---|---|---|
| Cifrado simétrico | AES-256-GCM | Para todos los datos locales |
| Derivación de clave | PBKDF2 + SHA-256 | 310,000 iteraciones (estándar OWASP 2024) |
| Almacén encriptado | SQLCipher | Un solo archivo .velo para todo |
| Certificate validation | System.Security.Cryptography | + validación CT logs manual |
| TLS inspection | WebView2 hooks | Interceptación antes de establecer conexión |

### 2.3 IA (Pluggable)

| Modo | Implementación | API Key requerida |
|---|---|---|
| Offline | Solo LocalRuleEngine + blocklists | No |
| Claude | Anthropic API — claude-sonnet-4-20250514 | Sí (Anthropic) |
| Custom LLM | Cualquier endpoint compatible OpenAI spec | Depende del proveedor |
| Ollama (local) | Endpoint local http://localhost:11434 | No (corre en la máquina) |

### 2.4 Blocklists

| Lista | Fuente | Update |
|---|---|---|
| EasyList | https://easylist.to/easylist/easylist.txt | Cada 7 días |
| EasyPrivacy | https://easylist.to/easylist/easyprivacy.txt | Cada 7 días |
| uBlock Origin Filters | https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt | Cada 7 días |
| uBlock Badware | https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/badware.txt | Cada 7 días |
| Peter Lowe's Ad List | https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts | Cada 7 días |

Las blocklists se descargan en background, nunca bloquean el arranque. Si no hay internet, se usan las del build anterior (embebidas en `resources/blocklists/`). En ese caso se muestra un toast silencioso en la esquina inferior derecha: *"Blocklists: usando versión del [fecha última actualización exitosa]"*. El toast desaparece solo a los 5 segundos sin requerir acción del usuario.

### 2.5 NuGet Packages (lista completa)

```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="*" />
<PackageReference Include="SQLitePCLRaw.bundle_sqlcipher" Version="*" />
<PackageReference Include="sqlite-net-pcl" Version="*" />
<PackageReference Include="Anthropic.SDK" Version="*" />
<PackageReference Include="System.Text.Json" Version="*" />
<PackageReference Include="ModernWpfUI" Version="*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="*" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="*" />
<PackageReference Include="Serilog.Sinks.File" Version="*" />
```

**Nota sobre logging:** Serilog escribe SOLO a archivo local en %AppData%\VELO\logs\. Nunca a ningún endpoint remoto. El log no incluye URLs visitadas, solo errores de la aplicación.

---

## 3. ESTRUCTURA DE REPOSITORIO

```
VELO/
├── VELO.sln
├── LICENSE                          ← AGPLv3
├── README.md
├── PRIVACY.md                       ← Qué datos se procesan, qué nunca
├── SECURITY.md                      ← Cómo reportar vulnerabilidades
│
├── src/
│   │
│   ├── VELO.App/                    ← Entry point WPF
│   │   ├── App.xaml
│   │   ├── App.xaml.cs              ← DI setup, arranque global
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   └── Startup/
│   │       ├── AppBootstrapper.cs   ← Inicializa todo en orden
│   │       └── DependencyConfig.cs  ← Registro de servicios
│   │
│   ├── VELO.UI/                     ← Componentes WPF
│   │   ├── Controls/
│   │   │   ├── BrowserTab.xaml / .cs
│   │   │   ├── TabBar.xaml / .cs
│   │   │   ├── UrlBar.xaml / .cs
│   │   │   ├── SecurityPanel.xaml / .cs
│   │   │   ├── NewTabPage.xaml / .cs
│   │   │   ├── DownloadBar.xaml / .cs
│   │   │   └── PasswordPrompt.xaml / .cs
│   │   ├── Dialogs/
│   │   │   ├── OnboardingWizard.xaml / .cs
│   │   │   ├── SettingsWindow.xaml / .cs
│   │   │   ├── ImportWizard.xaml / .cs
│   │   │   └── PasswordVaultWindow.xaml / .cs
│   │   ├── Pages/
│   │   │   └── AboutPage.xaml / .cs  ← velo://about
│   │   └── Themes/
│   │       ├── DarkTheme.xaml
│   │       └── Colors.xaml
│   │
│   ├── VELO.Core/                   ← Lógica de negocio
│   │   ├── Navigation/
│   │   │   ├── TabManager.cs
│   │   │   ├── NavigationController.cs
│   │   │   └── ContainerManager.cs
│   │   ├── Downloads/
│   │   │   └── DownloadManager.cs
│   │   ├── Bookmarks/
│   │   │   └── BookmarkService.cs
│   │   ├── History/
│   │   │   └── HistoryService.cs
│   │   └── Search/
│   │       └── SearchEngineService.cs
│   │
│   ├── VELO.Security/               ← Motor de seguridad
│   │   ├── Guards/
│   │   │   ├── RequestGuard.cs
│   │   │   ├── ScriptGuard.cs
│   │   │   ├── TLSGuard.cs
│   │   │   └── ContainerGuard.cs
│   │   ├── Fingerprint/
│   │   │   ├── FingerprintProtection.cs
│   │   │   └── NoiseGenerator.cs
│   │   ├── WebRTC/
│   │   │   └── WebRTCController.cs
│   │   ├── CookieWall/
│   │   │   ├── CookieWallEngine.cs
│   │   │   ├── ConsentInjector.cs
│   │   │   ├── DOMExtractor.cs
│   │   │   ├── CacheFetcher.cs
│   │   │   └── PaywallDetector.cs
│   │   ├── AI/
│   │   │   ├── AISecurityEngine.cs
│   │   │   ├── Adapters/
│   │   │   │   ├── IAIAdapter.cs        ← Interface común
│   │   │   │   ├── OfflineAdapter.cs
│   │   │   │   ├── ClaudeAdapter.cs
│   │   │   │   ├── OpenAIAdapter.cs
│   │   │   │   └── OllamaAdapter.cs
│   │   │   ├── LocalRuleEngine.cs
│   │   │   ├── SecurityCache.cs
│   │   │   └── Models/
│   │   │       ├── SecurityVerdict.cs
│   │   │       ├── ThreatContext.cs
│   │   │       └── VerdictType.cs
│   │   └── Rules/
│   │       ├── BlocklistManager.cs
│   │       └── BlocklistUpdater.cs
│   │
│   ├── VELO.DNS/                    ← DoH Engine
│   │   ├── DoHResolver.cs
│   │   ├── DoHProvider.cs
│   │   └── Providers/
│   │       ├── Quad9Provider.cs
│   │       ├── NextDNSProvider.cs
│   │       ├── CloudflareProvider.cs
│   │       └── CustomProvider.cs
│   │
│   ├── VELO.Data/                   ← Persistencia
│   │   ├── VeloDatabase.cs          ← Inicialización SQLCipher
│   │   ├── Repositories/
│   │   │   ├── HistoryRepository.cs
│   │   │   ├── BookmarkRepository.cs
│   │   │   ├── PasswordRepository.cs
│   │   │   ├── SecurityCacheRepository.cs
│   │   │   └── SettingsRepository.cs
│   │   └── Models/
│   │       ├── HistoryEntry.cs
│   │       ├── Bookmark.cs
│   │       ├── PasswordEntry.cs
│   │       ├── CachedVerdict.cs
│   │       └── AppSettings.cs
│   │
│   └── VELO.Vault/                  ← Password Manager
│       ├── VaultService.cs
│       ├── VaultCrypto.cs
│       ├── VaultExporter.cs
│       └── VaultImporter.cs
│
├── tests/
│   ├── VELO.Security.Tests/
│   ├── VELO.Core.Tests/
│   ├── VELO.DNS.Tests/
│   └── VELO.Vault.Tests/
│
└── resources/
    ├── blocklists/                  ← Snapshot inicial del build
    ├── scripts/
    │   ├── fingerprint-noise.js     ← Se inyecta en cada página
    │   ├── webrtc-spoof.js
    │   └── dom-extractor.js
    └── installer/
        └── VELO.msix.manifest
```

---

## 4. ARQUITECTURA DE CAPAS — ORDEN DE EJECUCIÓN REAL

Cada request pasa por las capas en este orden exacto. Una capa puede detener el flujo y devolver un resultado sin que las siguientes se ejecuten.

```
Usuario ingresa URL / página hace request
         │
         ▼
┌─────────────────────────┐
│  CAPA 6: WebView2       │  ← Intercepta el request ANTES de enviarlo
│  CoreWebView2.WebResourceRequested
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│  CAPA 5: DoH Engine     │  ← Resuelve DNS por HTTPS (Quad9 default)
│  Reemplaza DNS del sistema
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│  CAPA 3: Security Guards│
│  1. BlocklistManager    │  ← Check instantáneo contra 2M+ dominios
│     ¿En blocklist?      │
│     SÍ → BLOCK (0ms)    │
│     NO → continúa       │
│  2. TLSGuard            │  ← Valida certificado, CT logs, HSTS
│  3. RequestGuard        │  ← Analiza headers, patrones de URL, SSRF
│  4. ContainerGuard      │  ← Verifica que el request no cruce containers
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│  CAPA 4: AI Engine      │
│  1. Check SecurityCache │  ← ¿Ya analizamos este dominio?
│     HIT → usar resultado│
│     MISS → continúa     │
│  2. LocalRuleEngine     │  ← Heurísticas de scripts, patrones conocidos
│     ¿Determinante?      │
│     SÍ → resultado      │
│     NO → continúa       │
│  3. AI Adapter          │  ← Claude / OpenAI / Ollama (timeout 3s)
│     Timeout → Offline   │
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│  CAPA 2: CookieWall     │  ← Solo si se detecta consent wall/paywall
│  Bypass Engine          │
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│  CAPA 1: UI (WPF)       │  ← Muestra resultado al usuario
│  SecurityPanel          │  ← Veredicto + explicación si aplica
└─────────────────────────┘
```

---

## 5. CAPA 1 — UI (WPF)

### 5.1 MainWindow

La ventana principal contiene:
- `TabBar` en la parte superior (pestañas + botón nueva pestaña)
- `UrlBar` debajo del TabBar
- `BrowserTab` (WebView2) ocupa el resto del espacio
- `SecurityPanel` se desliza desde la derecha cuando hay un veredicto WARN o BLOCK
- `DownloadBar` aparece en la parte inferior cuando hay descargas activas

**Dimensiones mínimas:** 800x600px
**Tema:** Oscuro siempre. No hay modo claro. Es una decisión de diseño deliberada.

### 5.2 UrlBar

Componentes de izquierda a derecha:
1. Botón atrás / adelante
2. Botón reload / stop
3. Indicador TLS (🔒 verde / 🔓 rojo / ⚠️ amarillo)
4. Campo de URL (editable, muestra dominio destacado del resto de la URL)
5. Indicador modo IA (icono pequeño: 🧠 Claude / ⚙️ Offline / 🔌 Custom)
6. Indicador container actual (círculo de color si está en un container)
7. Botón menú (⋮)

**Comportamiento del campo URL:**
- Si el usuario escribe texto sin protocolo y sin punto, se trata como búsqueda (va a DuckDuckGo o el motor configurado)
- Si tiene punto o protocolo, se trata como URL
- Autocompletado solo desde historial local. Nunca desde un servidor externo.

### 5.3 SecurityPanel

Se abre automáticamente cuando hay veredicto WARN o BLOCK. El usuario puede fijarlo abierto.

Estructura del panel:

```
┌────────────────────────────────────┐
│ [ICONO] [TIPO DE VEREDICTO]   [X] │
│                                    │
│ DOMINIO/URL AFECTADA               │
│                                    │
│ POR QUÉ:                          │
│ [Explicación en lenguaje natural   │
│  generada por IA o por la regla    │
│  local que activó el bloqueo]      │
│                                    │
│ Tipo de amenaza: [texto]           │
│ Confianza: [%]                     │
│ Fuente: [IA / Blocklist / TLS /    │
│          Heurística]               │
│                                    │
│ [Ver detalles técnicos ▼]         │
│                                    │
│ [PERMITIR UNA VEZ] [WHITELIST]    │
└────────────────────────────────────┘
```

**Colores:**
- BLOCK: borde rojo (#FF3D71), icono 🔴
- WARN: borde amarillo (#FFB300), icono 🟡
- SAFE: no se muestra panel (silencioso)

**"Ver detalles técnicos"** expande una sección con:
- Headers HTTP de la request
- Hash del script (si aplica)
- Snippet del script analizado (primeros 200 chars, nunca datos de usuario)
- Respuesta raw del AI Adapter (si se usó IA)

### 5.4 NewTabPage

Generada 100% localmente. Sin llamadas de red al abrirse.

Contenido:
- Logo VELO centrado
- Barra de búsqueda (misma funcionalidad que UrlBar)
- Reloj digital (hora local, sin API)
- Grid de bookmarks recientes (máximo 8, imágenes de favicon en cache local)
- Nada más

**Favicon cache:** Los favicons se descargan una sola vez cuando el usuario visita el sitio y se guardan en SQLite. La NewTabPage los lee de ahí. Nunca hace requests para mostrar favicons.

### 5.5 Container Tabs

Cada tab tiene un container asignado. Los containers son aislamiento de sesión: cookies, localStorage, sessionStorage y caché son completamente separados entre containers.

**Containers por defecto (con colores):**
- Personal (azul #00E5FF)
- Trabajo (verde #7FFF5F)
- Banca (rojo #FF3D71)
- Compras (amarillo #FFB300)
- Sin container (gris — default)

El usuario puede crear containers adicionales con nombre y color custom.

**Indicador visual:** Tab con container muestra una línea del color del container en la parte inferior del tab.

**ContainerGuard** verifica que ningún script o request cruce el boundary de un container. Si un iframe en el container "Trabajo" intenta acceder a cookies del container "Personal", se bloquea.

### 5.6 PasswordVaultWindow

Ventana separada (no modal) para el gestor de contraseñas.

Features:
- Lista de entradas (sitio, usuario, fecha de modificación)
- Búsqueda local
- Autocompletado en formularios de login detectados por ScriptGuard
- Generador de contraseñas integrado (configurable: longitud, caracteres)
- Master password requerida al abrir (si el vault lleva más de 15 minutos sin actividad, pide master password de nuevo)
- Export a `.velovault` (archivo encriptado AES-256 con la master password)
- Import desde `.velovault` o desde CSV estándar de Chrome/Firefox/Bitwarden

---

## 6. CAPA 2 — COOKIE WALL BYPASS ENGINE

### 6.1 PaywallDetector

Se ejecuta después de que la página carga. Analiza el DOM buscando:

**Patrones de detección:**
```csharp
// Indicadores de consent wall
private static readonly string[] ConsentIndicators = {
    "cookie-consent", "cookie-banner", "gdpr", "consent-manager",
    "privacy-notice", "cookie-overlay", "cookielaw", "onetrust",
    "didomi", "quantcast", "trustarc", "cookiebot", "usercentrics"
};

// Indicadores de paywall
private static readonly string[] PaywallIndicators = {
    "paywall", "subscription-wall", "meter-wall", "article-locked",
    "premium-content", "subscriber-only", "register-wall"
};

// Indicadores de newsletter gate
private static readonly string[] NewsletterIndicators = {
    "newsletter-gate", "email-gate", "subscribe-to-read"
};
```

Si se detecta alguno: lanza el flujo de bypass en cascada.

### 6.2 Flujo en Cascada (4 estrategias)

**Estrategia 1 — ConsentInjector**

Inyecta cookies y localStorage entries que simulan haber aceptado solo el consentimiento mínimo funcional.

```javascript
// fingerprint-consent.js — se inyecta si se detecta consent wall
(function() {
    // Solo categorías funcionales/necesarias
    // NUNCA: analytics, marketing, advertising, social, profiling
    const minimalConsent = {
        "necessary": true,
        "functional": true,
        "analytics": false,
        "marketing": false,
        "advertising": false,
        "social": false,
        "profiling": false
    };
    
    // OneTrust
    localStorage.setItem('OptanonConsent', 'groups=C0001:1,C0002:0,C0003:0,C0004:0');
    localStorage.setItem('OptanonAlertBoxClosed', new Date().toISOString());
    
    // Cookiebot
    document.cookie = 'CookieConsent={stamp:\'none\',necessary:true,preferences:false,statistics:false,marketing:false}; path=/';
    
    // Didomi
    localStorage.setItem('didomi_token', btoa(JSON.stringify({user_id: 'velo-anonymous', created: Date.now(), updated: Date.now(), vendors: {enabled:[]}, purposes: {enabled:['cookies']}})));
    
    // Genérico
    localStorage.setItem('cookie_consent', 'functional_only');
    localStorage.setItem('gdpr_consent', '1');
    document.cookie = 'cookie_consent=functional; path=/; max-age=31536000';
})();
```

**Disclaimer legal** (incluir en PRIVACY.md y en UI):
> VELO no elude el consentimiento de tracking. Solo acepta automáticamente las cookies estrictamente necesarias para el funcionamiento del sitio. Las cookies de analytics, marketing, publicidad y perfilado nunca se aceptan.

**Estrategia 2 — DOMExtractor**

Si la Estrategia 1 falla (el sitio verifica server-side), extrae el contenido del artículo del DOM antes de que el overlay lo tape y activa Reader Mode.

```javascript
// dom-extractor.js
(function() {
    // Selectores de contenido principal (por orden de prioridad)
    const contentSelectors = [
        'article',
        '[role="main"]',
        '.article-body',
        '.post-content', 
        '.entry-content',
        '#article-body',
        'main p'
    ];
    
    // Selectores de overlay a remover
    const overlaySelectors = [
        '.paywall', '.cookie-wall', '.consent-overlay',
        '[class*="paywall"]', '[class*="overlay"]',
        '[id*="paywall"]', '[id*="consent"]'
    ];
    
    // Extraer contenido antes de que el overlay bloquee
    // ...
})();
```

Reader Mode presenta el contenido extraído en una vista limpia (fuente grande, fondo oscuro, sin imágenes de tracking).

**Estrategia 3 — CacheFetcher**

Si el DOM no tiene contenido extraíble, busca versión cacheada:

```csharp
public async Task<string?> FetchCachedVersion(string url)
{
    // Intentar Google Cache
    var googleCacheUrl = $"https://webcache.googleusercontent.com/search?q=cache:{Uri.EscapeDataString(url)}";
    
    // Si Google Cache falla, intentar archive.ph
    var archiveUrl = $"https://archive.ph/newest/{Uri.EscapeDataString(url)}";
    
    // Retorna el HTML de la versión cacheada o null
}
```

**Estrategia 4 — Notificación al usuario**

Si las tres estrategias fallan, muestra en la barra de URL:
> "Este sitio requiere cookies de tracking para mostrar contenido. [Ver en caché] [Continuar de todas formas] [Ignorar siempre]"

### 6.3 Configuración del CookieWall Engine

En Settings → Privacidad → Cookie Wall Bypass:
- ☑ Activar ConsentInjector (default: ON)
- ☑ Activar DOMExtractor + Reader Mode automático (default: ON)
- ☑ Activar CacheFetcher (default: ON)
- ☐ Notificar cuando un bypass tiene éxito (default: OFF — silencioso)

---

## 7. CAPA 3 — SECURITY GUARDS

### 7.1 RequestGuard

Se engancha en `CoreWebView2.WebResourceRequested`. Se ejecuta para CADA request (documentos, imágenes, scripts, XHR, fetch, beacons).

**Flujo interno:**

```csharp
public async Task<RequestVerdict> EvaluateRequest(WebResourceRequest request)
{
    var url = new Uri(request.Uri);
    
    // 1. Whitelist del usuario (siempre pasa primero)
    if (_userWhitelist.Contains(url.Host)) 
        return RequestVerdict.Allow;
    
    // 2. Blocklist local (instantáneo)
    if (_blocklistManager.IsBlocked(url.Host))
        return RequestVerdict.Block("Dominio en blocklist", ThreatType.KnownTracker);
    
    // 3. Detección de DNS rebinding
    if (await IsDnsRebindingAttempt(url))
        return RequestVerdict.Block("Posible ataque DNS rebinding", ThreatType.DnsRebinding);
    
    // 4. Detección de SSRF (request a IPs privadas desde página pública)
    if (IsPrivateIpRequest(url) && !IsLocalPage(request.Referrer))
        return RequestVerdict.Block("Request a IP privada desde página externa", ThreatType.SSRF);
    
    // 5. Detección de data exfiltration en URL
    if (HasSuspiciousUrlParams(url))
        return RequestVerdict.Warn("URL contiene parámetros sospechosos", ThreatType.DataExfiltration);
    
    // 6. Tracking pixels y beacons conocidos
    if (IsTrackingBeacon(url, request.Headers))
        return RequestVerdict.Block("Tracking beacon detectado", ThreatType.Tracker);
    
    // 7. Mixed content (HTTP desde HTTPS)
    if (IsMixedContent(url, request.Referrer))
        return RequestVerdict.Warn("Contenido mixto HTTP/HTTPS", ThreatType.MixedContent);
    
    // 8. Si ninguna regla es determinante, pasar a AI Engine
    return RequestVerdict.NeedsAIAnalysis;
}
```

**Detección de data exfiltration en URL:**
```csharp
private bool HasSuspiciousUrlParams(Uri url)
{
    var query = HttpUtility.ParseQueryString(url.Query);
    foreach (string key in query.Keys)
    {
        var value = query[key] ?? "";
        // Detectar base64 largo (posible dato codificado)
        if (value.Length > 50 && IsBase64(value)) return true;
        // Detectar emails en params
        if (Regex.IsMatch(value, @"[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}")) return true;
        // Detectar UUIDs (posibles session IDs siendo exfiltrados)
        if (value.Length > 100) return true;
    }
    return false;
}
```

### 7.2 ScriptGuard

Analiza scripts ANTES de que WebView2 los ejecute usando `CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync` para inyectar el analizador, y `CoreWebView2.WebResourceRequested` para interceptar scripts externos.

**Scoring de riesgo (0-100):**

| Patrón detectado | Puntos de riesgo |
|---|---|
| `eval()` con string dinámico | +20 |
| `document.cookie` accedido | +10 |
| `navigator.sendBeacon()` | +15 |
| `fetch()` a dominio diferente al origen | +10 |
| `XMLHttpRequest` a dominio diferente | +10 |
| `navigator.plugins` enumerado | +15 |
| `navigator.hardwareConcurrency` leído | +10 |
| `screen.width/height` + `devicePixelRatio` juntos | +20 |
| `AudioContext` creado | +15 |
| `WebGL` renderer info accedido | +20 |
| `localStorage` escritura masiva (>10KB) | +15 |
| `document.write()` | +10 |
| `window.location` modificado silenciosamente | +25 |
| Ofuscación detectada (hex encoding, string split) | +30 |

**Umbrales:**
- 0-30: SAFE — ejecutar normalmente
- 31-60: WARN — ejecutar pero notificar al usuario
- 61-80: AI Analysis — enviar a AI Engine para análisis profundo
- 81+: BLOCK — no ejecutar, notificar

**Crypto miner detection:**
```csharp
private static readonly string[] MinerPatterns = {
    "coinhive", "cryptonight", "monero", "stratum+tcp",
    "minero", "webminerpool", "coinimp", "jsecoin"
};
```

### 7.3 TLSGuard

Se ejecuta en `CoreWebView2.ServerCertificateErrorDetected` y mediante análisis previo de la URL.

**Checks implementados:**

1. **Certificate Transparency (CT) logs**
   - Consulta a https://crt.sh para verificar que el certificado está en CT logs públicos
   - Si el certificado NO está en CT logs: BLOCK con explicación
   - Cache de resultados CT: 24 horas por dominio

2. **HSTS verification**
   - Lista HSTS preload embebida (chromium-hsts-preload-list.json)
   - Si el sitio está en preload list y sirve HTTP: BLOCK

3. **Certificate pinning para sitios críticos**
   - Lista hardcodeada de pins para: google.com, facebook.com, twitter.com, github.com, etc.
   - Si el pin no coincide: BLOCK con mensaje "Posible ataque Man-in-the-Middle"

4. **Self-signed certificate detection**
   - Si el certificado es self-signed en un sitio público: WARN
   - Si es self-signed en localhost o IP local: ALLOW (caso legítimo de desarrollo)

5. **Cipher suite weakness**
   - Si negocia TLS < 1.2: WARN
   - Si negocia TLS 1.0 o SSL: BLOCK
   - Si usa cipher suites deprecados (RC4, DES, NULL): BLOCK

6. **OCSP stapling**
   - Verifica que el certificado no esté revocado
   - Si OCSP falla y no hay stapling: WARN (no BLOCK, para no romper sitios)

### 7.4 ContainerGuard

Verifica que las requests no crucen boundaries de containers.

```csharp
public RequestVerdict EvaluateContainerIsolation(
    WebResourceRequest request, 
    string currentContainer, 
    string requestDestinationContainer)
{
    if (currentContainer == requestDestinationContainer) 
        return RequestVerdict.Allow;
    
    // Permite requests a dominios de terceros (CDN, APIs)
    // Bloquea acceso a cookies/storage de otro container
    if (request.ResourceType == ResourceType.XmlHttpRequest ||
        request.ResourceType == ResourceType.Fetch)
    {
        // Verificar que no intenta acceder a cookies del otro container
        if (HasCrossContainerCookieAccess(request))
            return RequestVerdict.Block(
                "Intento de acceso a datos de otro container", 
                ThreatType.ContainerViolation);
    }
    
    return RequestVerdict.Allow;
}
```

---

## 8. CAPA 4 — AI SECURITY ENGINE (PLUGGABLE)

### 8.1 Interface IAIAdapter

```csharp
public interface IAIAdapter
{
    bool IsAvailable { get; }
    string ModeName { get; }
    Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct);
}
```

### 8.2 ThreatContext — Qué se envía exactamente a la IA

**CRÍTICO: Qué se incluye en el análisis:**

```csharp
public class ThreatContext
{
    // El dominio siendo analizado (ej: "tracking.adtech.io")
    public string Domain { get; set; }
    
    // Tipo de request: Document, Script, XHR, Image, etc.
    public string ResourceType { get; set; }
    
    // Dominio de origen (ej: "nytimes.com")
    public string Referrer { get; set; }
    
    // Hash SHA-256 del script (NO el script completo)
    public string? ScriptHash { get; set; }
    
    // Primeros 500 caracteres del script (NO el script completo)
    // Solo si ScriptGuard score > 60
    public string? ScriptSnippet { get; set; }
    
    // Patrones detectados por ScriptGuard
    public List<string> DetectedPatterns { get; set; }
    
    // Score de riesgo del ScriptGuard (0-100)
    public int RiskScore { get; set; }
    
    // Info TLS básica (versión, si es self-signed)
    public TLSInfo? TLSInfo { get; set; }
    
    // Headers de respuesta HTTP (sin cookies, sin Authorization)
    public Dictionary<string, string> ResponseHeaders { get; set; }
}
```

**QUÉ NUNCA SE ENVÍA A LA IA:**
- Contenido completo de la página
- Cookies del usuario
- Headers de Authorization
- Datos del formulario
- Historial de navegación
- Contraseñas
- Datos personales del usuario
- URL completa con query params si contienen datos de usuario

Esta lista va en PRIVACY.md y en la UI (Settings → IA → "¿Qué datos se analizan?").

### 8.3 ClaudeAdapter

```csharp
public class ClaudeAdapter : IAIAdapter
{
    private const string DEFAULT_MODEL = "claude-sonnet-4-20250514";
    
    // El modelo es configurable en Settings → IA → Modelo Claude
    // para no requerir recompilación cuando Anthropic actualice nombres.
    // Default hardcodeado: claude-sonnet-4-20250514
    private string Model => _settings.ClaudeModel ?? DEFAULT_MODEL;
    private const int MAX_TOKENS = 300; // La respuesta es JSON corto
    private const int TIMEOUT_SECONDS = 3;
    
    public async Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct)
    {
        var prompt = BuildPrompt(context);
        
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
        
        try
        {
            var response = await _anthropicClient.Messages.CreateAsync(
                new MessageRequest
                {
                    Model = MODEL,
                    MaxTokens = MAX_TOKENS,
                    System = SYSTEM_PROMPT,
                    Messages = [new() { Role = "user", Content = prompt }]
                }, 
                timeoutCts.Token);
            
            return ParseVerdict(response.Content[0].Text);
        }
        catch (OperationCanceledException)
        {
            // Timeout — fallback a Offline
            return AIVerdict.Fallback("IA no disponible, usando análisis local");
        }
    }
    
    private const string SYSTEM_PROMPT = @"
Eres un analizador de seguridad web. Recibes contexto técnico sobre una request de red y debes determinar si es una amenaza.

RESPONDE ÚNICAMENTE con JSON válido, sin texto adicional, sin markdown:
{
  ""verdict"": ""SAFE"" | ""WARN"" | ""BLOCK"",
  ""confidence"": 0-100,
  ""reason"": ""Explicación breve en español (máximo 2 oraciones)"",
  ""threat_type"": ""Tracker"" | ""Malware"" | ""Phishing"" | ""DataExfiltration"" | ""Miner"" | ""Fingerprinting"" | ""MitM"" | ""Other"" | null
}

Criterios:
- BLOCK: amenaza clara, riesgo alto para el usuario
- WARN: comportamiento sospechoso pero no definitivamente malicioso  
- SAFE: request legítima, sin indicadores de amenaza
- confidence: qué tan seguro estás de tu veredicto
- reason: en español, que el usuario no técnico entienda qué pasó
";
}
```

### 8.4 OfflineAdapter

```csharp
public class OfflineAdapter : IAIAdapter
{
    public bool IsAvailable => true; // Siempre disponible
    public string ModeName => "Offline";
    
    public Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct)
    {
        // Solo usa el RiskScore del ScriptGuard y los patrones detectados
        if (context.RiskScore >= 70)
            return Task.FromResult(AIVerdict.Block(
                $"Script sospechoso detectado: {string.Join(", ", context.DetectedPatterns)}",
                "Heurística local"));
        
        if (context.RiskScore >= 40)
            return Task.FromResult(AIVerdict.Warn(
                $"Script con comportamiento inusual: {string.Join(", ", context.DetectedPatterns)}",
                "Heurística local"));
        
        return Task.FromResult(AIVerdict.Safe());
    }
}
```

### 8.5 OllamaAdapter

```csharp
public class OllamaAdapter : IAIAdapter
{
    private readonly string _endpoint; // default: http://localhost:11434
    private readonly string _model;   // configurable: llama3, mistral, etc.
    
    public async Task<AIVerdict> AnalyzeAsync(ThreatContext context, CancellationToken ct)
    {
        // Mismo formato de prompt que ClaudeAdapter
        // Usa OpenAI-compatible API que Ollama expone en /v1/chat/completions
        var requestBody = new
        {
            model = _model,
            messages = new[] {
                new { role = "system", content = SYSTEM_PROMPT },
                new { role = "user", content = BuildPrompt(context) }
            },
            stream = false
        };
        
        // POST a http://localhost:11434/v1/chat/completions
        // Parse respuesta igual que OpenAI
    }
}
```

### 8.6 SecurityCache

```csharp
public class SecurityCache
{
    // TTL por tipo de veredicto
    private static readonly Dictionary<VerdictType, TimeSpan> TTL = new()
    {
        { VerdictType.Safe,  TimeSpan.FromHours(24) },
        { VerdictType.Warn,  TimeSpan.FromHours(1)  },
        { VerdictType.Block, TimeSpan.MaxValue       } // Permanente hasta override manual
    };
    
    // Cache key = SHA-256(domain + resourceType + scriptHash?)
    public async Task<CachedVerdict?> GetAsync(ThreatContext context)
    {
        var key = ComputeCacheKey(context);
        var cached = await _repo.GetByKeyAsync(key);
        
        if (cached == null) return null;
        if (DateTime.UtcNow - cached.CachedAt > TTL[cached.VerdictType]) 
        {
            await _repo.DeleteAsync(key);
            return null;
        }
        
        return cached;
    }
}
```

---

## 9. CAPA 5 — DNS PRIVADO (DoH ENGINE)

### 9.1 Implementación

WebView2 no tiene un hook nativo para interceptar DNS. La solución es levantar un proxy HTTP local en `127.0.0.1:5353` que intercepta todas las queries DNS y las resuelve vía DoH, y luego indicarle a WebView2 que use ese proxy.

**Integración exacta con WebView2:**

```csharp
// En AppBootstrapper.cs — antes de inicializar WebView2
int assignedPort = await _doHProxyServer.StartAsync("127.0.0.1");

// En la inicialización de WebView2
var options = new CoreWebView2EnvironmentOptions
{
    AdditionalBrowserArguments = 
        $"--proxy-server=http://127.0.0.1:{assignedPort} " +
        "--disable-features=msEdgeSidebarV2 ..."
};
```

**Implementación del proxy DoH local:**
- Usar `System.Net.HttpListener` propio (sin dependencias externas)
- **Puerto default: 5354** — NUNCA 5353 (es mDNS/Bonjour, conflicto real en Windows con iTunes, impresoras, Spotify)
- Puerto configurable en Settings → Avanzado → "DoH Proxy Port" (rango 5000-6000)
- Si el puerto elegido falla (`AddressAlreadyInUse`): intentar automáticamente 5355, 5356... hasta 5360
- Si todos fallan → fallback silencioso a DNS del sistema + toast de advertencia una sola vez

```csharp
public async Task<int> StartAsync(string host, int preferredPort = 5354)
{
    for (int port = preferredPort; port <= preferredPort + 6; port++)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{host}:{port}/");
            _listener.Start();
            _logger.LogInformation("DoH proxy started on port {Port}", port);
            return port;
        }
        catch (HttpListenerException)
        {
            continue; // Puerto ocupado — intentar el siguiente
        }
    }
    _eventBus.Publish(new DoHProxyFailedEvent());
    return -1; // Señal de fallback a DNS del sistema
}
```

```csharp
public class DoHResolver
{
    public async Task<IPAddress[]> ResolveAsync(string hostname)
    {
        // 1. Check cache local (TTL según respuesta DNS)
        if (_dnsCache.TryGet(hostname, out var cached)) return cached;
        
        // 2. Resolver vía DoH
        var provider = _settings.DoHProvider; // Quad9, NextDNS, Cloudflare, Custom
        var result = await provider.ResolveAsync(hostname);
        
        // 3. Verificar que la IP resuelta no sea sospechosa
        // (ej: dominio público que resuelve a IP privada = DNS rebinding)
        if (IsPublicDomain(hostname) && IsPrivateIP(result))
            throw new DnsRebindingException(hostname, result);
        
        _dnsCache.Set(hostname, result, result.TTL);
        return result.Addresses;
    }
}
```

### 9.2 Providers

**Quad9 (default):**
```
DoH URL: https://dns.quad9.net/dns-query
Características: Bloquea malware conocido por DNS, sin logs, Suiza
```

**NextDNS:**
```
DoH URL: https://dns.nextdns.io/{config_id}
Características: Configurable con listas custom, free tier 300K queries/mes
```

**Cloudflare 1.1.1.1:**
```
DoH URL: https://cloudflare-dns.com/dns-query
Características: Más rápido, retiene logs por 24h
```

**Custom:**
```
El usuario ingresa cualquier URL DoH compatible con RFC 8484
```

---

## 10. CAPA 6 — WEBVIEW2 ENGINE

### 10.1 Inicialización con Zero Telemetría

```csharp
public async Task InitializeWebView2Async()
{
    var options = new CoreWebView2EnvironmentOptions
    {
        // Desactivar TODA telemetría de Edge/Microsoft
        AdditionalBrowserArguments = string.Join(" ", new[]
        {
            "--disable-features=msEdgeSidebarV2",
            "--disable-features=EdgeShoppingAssistant", 
            "--disable-features=EdgeCollections",
            "--disable-features=msSaveMHtmlFile",
            "--disable-crash-reporter",
            "--disable-breakpad",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-networking",
            "--disable-client-side-phishing-detection",
            "--disable-sync",
            "--disable-translate",
            "--disable-extensions",
            "--disable-plugins",
            "--metrics-recording-only",
            "--disable-logging",
            "--disable-hang-monitor",
            "--disable-prompt-on-repost",
            "--disable-domain-reliability",
            "--disable-component-update",
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding"
        })
    };
    
    var env = await CoreWebView2Environment.CreateAsync(
        browserExecutableFolder: null,  // Usa WebView2 del sistema
        userDataFolder: _userDataPath,  // %AppData%\VELO\Profile
        options: options
    );
    
    await _webView.EnsureCoreWebView2Async(env);
    
    // Configurar después de inicializar
    _webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
    _webView.CoreWebView2.Settings.IsScriptEnabled = true;
    _webView.CoreWebView2.Settings.IsWebMessageEnabled = false;
    _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
    _webView.CoreWebView2.Settings.AreDevToolsEnabled = false; // En producción
    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
    _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false; // Manejamos nosotros
    _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;  // Manejamos nosotros
}
```

### 10.2 Hooks de Intercepción

```csharp
// HOOK 1: Interceptar todas las requests
_webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
_webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

// HOOK 2: Certificados TLS
_webView.CoreWebView2.ServerCertificateErrorDetected += OnCertificateError;

// HOOK 3: Navegación (para DoH y TLSGuard previo)
_webView.CoreWebView2.NavigationStarting += OnNavigationStarting;

// HOOK 4: DOM listo (para PaywallDetector y fingerprint injection)
_webView.CoreWebView2.DOMContentLoaded += OnDOMContentLoaded;

// HOOK 5: Nueva ventana (para abrir en tab, no en ventana)
_webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

// HOOK 6: Permisos (cámara, micrófono, etc.)
_webView.CoreWebView2.PermissionRequested += OnPermissionRequested;
```

### 10.3 Fingerprint Protection Scripts

Se inyectan en TODOS los documentos antes de que ejecuten cualquier script de la página:

```javascript
// fingerprint-noise.js — inyectado via AddScriptToExecuteOnDocumentCreatedAsync

(function() {
    'use strict';
    
    // 1. Canvas noise
    const origToDataURL = HTMLCanvasElement.prototype.toDataURL;
    const origGetImageData = CanvasRenderingContext2D.prototype.getImageData;
    const origToBlob = HTMLCanvasElement.prototype.toBlob;
    
    HTMLCanvasElement.prototype.toDataURL = function(...args) {
        const result = origToDataURL.apply(this, args);
        return addNoise(result);
    };
    
    // 2. WebGL spoof
    const getParameter = WebGLRenderingContext.prototype.getParameter;
    WebGLRenderingContext.prototype.getParameter = function(parameter) {
        if (parameter === 37445) return 'Intel Inc.';          // UNMASKED_VENDOR_WEBGL
        if (parameter === 37446) return 'Intel Iris OpenGL';   // UNMASKED_RENDERER_WEBGL
        return getParameter.apply(this, arguments);
    };
    
    // 3. AudioContext fingerprint
    const origCreateAnalyser = AudioContext.prototype.createAnalyser;
    AudioContext.prototype.createAnalyser = function() {
        const analyser = origCreateAnalyser.apply(this);
        const origGetFloatFrequencyData = analyser.getFloatFrequencyData.bind(analyser);
        analyser.getFloatFrequencyData = function(array) {
            origGetFloatFrequencyData(array);
            for (let i = 0; i < array.length; i++) {
                array[i] += (Math.random() - 0.5) * 0.1; // Ruido mínimo
            }
        };
        return analyser;
    };
    
    // 4. Hardware concurrency spoof
    Object.defineProperty(navigator, 'hardwareConcurrency', {
        get: () => 4 // Siempre reporta 4, independientemente del hardware real
    });
    
    // 5. Device memory spoof
    Object.defineProperty(navigator, 'deviceMemory', {
        get: () => 8 // Siempre reporta 8GB
    });
    
    // 6. Font enumeration prevention
    // CSS font-face queries returns generic results
    
    // 7. Client Hints — respondidos con valores genéricos
    // Esto se maneja a nivel de WebView2 headers, no JS
    
    function addNoise(dataUrl) {
        // Implementación real — approach Mullvad Browser / LibreWolf
        // Ruido imperceptible (±1-2 en últimos bits) basado en seed de sesión
        // La seed se genera al arrancar VELO (inyectada desde C# via AddScriptToExecuteOnDocumentCreatedAsync)
        // y nunca sale del dispositivo
        const seed = window.__VELO_SESSION_SEED || 
                     (window.__VELO_SESSION_SEED = Math.random().toString(36) + Date.now());
        
        return dataUrl.replace(
            /data:image\/png;base64,([A-Za-z0-9+/=]+)/,
            (match, base64) => {
                const binary = atob(base64);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < bytes.length; i++) {
                    // XOR con seed — ruido determinístico por sesión, imperceptible visualmente
                    bytes[i] = binary.charCodeAt(i) ^ 
                               ((i * seed.charCodeAt(i % seed.length)) & 0x03);
                }
                return 'data:image/png;base64,' + btoa(String.fromCharCode(...bytes));
            }
        );
    }
})();
```

**Niveles de fingerprint:**

| Nivel | Canvas | WebGL | Audio | HW Concurrency | User Agent |
|---|---|---|---|---|---|
| Normal (Brave-like) | Noise leve | Spoof básico | No | No | Estático por sesión |
| Agresivo (DEFAULT) | Noise por sesión | Spoof completo | Noise | Fijo en 4 | Rotación por sesión |
| Máximo (Bunker) | Bloqueado | Bloqueado | Bloqueado | Fijo en 2 | Rotación por tab |

### 10.4 WebRTC Control

```javascript
// webrtc-spoof.js — inyectado según modo de seguridad

// Modo Normal: spoof IP
const origRTCPeerConnection = window.RTCPeerConnection;
window.RTCPeerConnection = function(config) {
    // Modificar ICE candidates para no revelar IP local real
    const pc = new origRTCPeerConnection(config);
    // ... spoof implementation
    return pc;
};

// Modo Paranoico/Bunker: desactivar completamente
// window.RTCPeerConnection = undefined;
// window.webkitRTCPeerConnection = undefined;
// window.mozRTCPeerConnection = undefined;
```

---

## 11. MÓDULOS TRANSVERSALES

### 11.1 Password Vault (VELO.Vault)

**Arquitectura de cifrado:**
```
Master Password
      │
      ▼
PBKDF2-SHA256 (310,000 iteraciones + salt único por instalación)
      │
      ▼
AES-256-GCM Key
      │
      ▼
SQLCipher database (todo el vault encriptado)
```

**VaultService.cs — operaciones principales:**

```csharp
public class VaultService
{
    // Desbloquear vault con master password
    public async Task<bool> UnlockAsync(string masterPassword);
    
    // Auto-lock después de inactividad
    public void StartAutoLockTimer(TimeSpan timeout); // default: 15 minutos
    
    // CRUD de entradas
    public Task<List<PasswordEntry>> GetAllAsync();
    public Task SaveAsync(PasswordEntry entry);
    public Task DeleteAsync(Guid entryId);
    
    // Generador de contraseñas
    public string GeneratePassword(int length = 24, 
                                   bool uppercase = true, 
                                   bool numbers = true, 
                                   bool symbols = true);
    
    // Export / Import
    public Task ExportAsync(string filePath); // .velovault (encriptado)
    public Task ImportAsync(string filePath, string password);
    public Task ImportFromCsvAsync(string filePath, CsvFormat format); 
    // CsvFormat: Chrome, Firefox, Bitwarden
}
```

**Estructura de PasswordEntry:**
```csharp
public class PasswordEntry
{
    public Guid Id { get; set; }
    public string SiteName { get; set; }
    public string Url { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }      // Almacenada cifrada
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? ContainerId { get; set; }  // Asociar a container
}
```

### 11.2 BlocklistManager

```csharp
public class BlocklistManager
{
    // Estructura en memoria: HashSet para O(1) lookup
    private HashSet<string> _blockedDomains = new();
    private HashSet<string> _blockedPatterns = new(); // Regex patterns
    
    public bool IsBlocked(string domain)
    {
        // Check dominio exacto
        if (_blockedDomains.Contains(domain)) return true;
        
        // Check subdominio (ej: "sub.tracker.com" → check "tracker.com")
        var parts = domain.Split('.');
        for (int i = 1; i < parts.Length - 1; i++)
        {
            var parent = string.Join('.', parts.Skip(i));
            if (_blockedDomains.Contains(parent)) return true;
        }
        
        return false;
    }
    
    public async Task UpdateAsync() // Llamado cada 7 días en background
    {
        foreach (var source in BLOCKLIST_SOURCES)
        {
            try
            {
                var content = await DownloadAsync(source.Url);
                ParseAndMerge(content, source.Format);
            }
            catch
            {
                // Sin internet: usar blocklists embebidas del build anterior
                // No lanzar excepción — el navegador sigue funcionando
                _eventBus.Publish(new BlocklistUpdateFailedEvent(
                    lastSuccessfulUpdate: _settings.BlocklistsLastUpdate));
                return;
            }
        }
        
        _settings.BlocklistsLastUpdate = DateTime.UtcNow;
        SaveToSQLite(); // Persiste para arranque sin internet
    }
}
```

### 11.3 HistoryService

```csharp
public class HistoryService
{
    // Guardar entrada (solo si el modo lo permite)
    public async Task RecordAsync(string url, string title, string? containerId)
    {
        if (_settings.SecurityMode == SecurityMode.Bunker) return; // Bunker: sin historial
        
        await _repo.SaveAsync(new HistoryEntry
        {
            Url = url,
            Title = title,
            ContainerId = containerId,
            VisitedAt = DateTime.UtcNow
        });
    }
    
    // Clear on exit (Paranoico y Bunker)
    public async Task ClearIfRequiredAsync()
    {
        if (_settings.SecurityMode >= SecurityMode.Paranoico)
            await _repo.ClearAllAsync();
    }
}
```

### 11.4 AutoUpdater

```csharp
public class AutoUpdater
{
    private const string UPDATE_CHECK_URL = 
        "https://api.github.com/repos/velo-browser/velo/releases/latest";
    
    public async Task CheckForUpdateAsync()
    {
        try
        {
            var latest = await FetchLatestVersionAsync();
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            
            if (latest > current)
            {
                // Notificar en UI — NUNCA instalar automáticamente
                _eventBus.Publish(new UpdateAvailableEvent(latest, _releaseNotes));
            }
        }
        catch
        {
            // Si falla el check: silencioso, no molestar al usuario
        }
    }
}
```

---

## 12. FLUJOS CRÍTICOS DETALLADOS

### 12.1 Primer Arranque — Onboarding Wizard

**Paso 0 (automático, invisible):**
- Crear directorio %AppData%\VELO\
- Inicializar SQLCipher database
- Cargar blocklists del bundle (las del build, sin descargar nada)
- Iniciar descarga de blocklists actualizadas en background

**Paso 1 — Modo IA:**
```
┌─────────────────────────────────────┐
│  VELO                               │
│                                     │
│  Elige cómo VELO analiza amenazas   │
│                                     │
│  ○ Offline (Recomendado)            │
│    100% local. Sin API key.         │
│    Seguro para uso diario.          │
│                                     │
│  ○ Claude (Mejor detección)         │
│    Requiere API key de Anthropic.   │
│    Análisis más profundo.           │
│    [Campo: API Key _____________]   │
│                                     │
│  ○ LLM Personalizado                │
│    Ollama u otro endpoint.          │
│    [Campo: Endpoint ____________]   │
│    [Campo: Modelo _____________]    │
│                                     │
│                    [Continuar →]    │
└─────────────────────────────────────┘
```

**Paso 2 — DNS Privado:**
```
┌─────────────────────────────────────┐
│  DNS Privado                        │
│                                     │
│  Tu ISP puede ver qué sitios        │
│  visitas con DNS normal.            │
│  VELO usa DNS encriptado.           │
│                                     │
│  ● Quad9 (Recomendado)             │
│    Sin logs · Bloquea malware       │
│    · Suiza                          │
│                                     │
│  ○ Cloudflare 1.1.1.1              │
│  ○ NextDNS                         │
│  ○ Personalizado                   │
│                                     │
│  [← Atrás]          [Continuar →]  │
└─────────────────────────────────────┘
```

**Paso 3 — Fingerprint:**
```
┌─────────────────────────────────────┐
│  Protección de Identidad            │
│                                     │
│  Los sitios pueden identificarte    │
│  sin cookies usando tu "huella      │
│  digital" del navegador.            │
│                                     │
│  VELO ya tiene activada:            │
│  ✓ Protección Canvas               │
│  ✓ Spoof de hardware               │
│  ✓ Rotación de User Agent          │
│  ✓ Protección WebRTC               │
│                                     │
│  Nivel actual: AGRESIVO (óptimo)   │
│                                     │
│  Puedes cambiarlo en Settings       │
│  en cualquier momento.              │
│                                     │
│  [← Atrás]          [Empezar →]   │
└─────────────────────────────────────┘
```

**Después del wizard (automático):**
- Preguntar si importar desde Chrome/Firefox/Edge si se detectan instalados
- Pedir crear master password para el Password Vault (obligatorio)
- Abrir NewTabPage

### 12.2 Import desde Otros Navegadores

```csharp
public class BrowserImporter
{
    public async Task ImportAsync(BrowserType browser, ImportOptions options)
    {
        var data = browser switch
        {
            BrowserType.Chrome  => await ReadChromeData(),
            BrowserType.Firefox => await ReadFirefoxData(),
            BrowserType.Edge    => await ReadEdgeData(),
            _ => throw new NotSupportedException()
        };
        
        if (options.ImportBookmarks)
            await _bookmarkService.ImportAsync(data.Bookmarks);
        
        if (options.ImportPasswords)
        {
            // Re-encriptar con AES-256 del vault ANTES de guardar
            foreach (var pwd in data.Passwords)
                await _vaultService.SaveAsync(pwd);
        }
        
        // NUNCA importar: cookies, historial de búsqueda, extensiones
    }
}
```

**Rutas de datos por navegador:**
```
Chrome:   %LOCALAPPDATA%\Google\Chrome\User Data\Default\
Firefox:  %APPDATA%\Mozilla\Firefox\Profiles\*.default\
Edge:     %LOCALAPPDATA%\Microsoft\Edge\User Data\Default\
```

### 12.3 Clear on Exit

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    var mode = _settings.SecurityMode;
    
    if (mode >= SecurityMode.Paranoico)
    {
        // Síncrono en el close para garantizar que se ejecuta
        _historyService.ClearAll();
        _webView.CoreWebView2.CookieManager.DeleteAllCookies();
        // Limpiar caché de WebView2
        await _webView.CoreWebView2.ExecuteScriptAsync("localStorage.clear(); sessionStorage.clear();");
    }
    
    if (mode == SecurityMode.Bunker)
    {
        // Adicionalmente: limpiar caché de DNS, caché de seguridad temporal
        _dnsCache.Clear();
    }
    
    base.OnClosing(e);
}
```

---

## 13. MODELOS DE DATOS (SQLite con SQLCipher)

### 13.1 Schema completo

```sql
-- Historial de navegación
CREATE TABLE history (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    url         TEXT NOT NULL,
    title       TEXT,
    container_id TEXT,
    visited_at  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    favicon_blob BLOB
);
CREATE INDEX idx_history_visited ON history(visited_at DESC);
CREATE INDEX idx_history_url ON history(url);

-- Bookmarks
CREATE TABLE bookmarks (
    id          TEXT PRIMARY KEY, -- UUID
    url         TEXT NOT NULL,
    title       TEXT NOT NULL,
    folder      TEXT DEFAULT 'root',
    container_id TEXT,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    favicon_blob BLOB
);

-- Password Vault
CREATE TABLE passwords (
    id          TEXT PRIMARY KEY, -- UUID
    site_name   TEXT NOT NULL,
    url         TEXT NOT NULL,
    username    TEXT NOT NULL,
    password    TEXT NOT NULL, -- Cifrado AES-256
    notes       TEXT,          -- Cifrado AES-256
    container_id TEXT,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    modified_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Caché de veredictos de seguridad
CREATE TABLE security_cache (
    cache_key   TEXT PRIMARY KEY, -- SHA-256(domain+resourceType+scriptHash)
    domain      TEXT NOT NULL,
    verdict     TEXT NOT NULL,    -- SAFE, WARN, BLOCK
    confidence  INTEGER,
    reason      TEXT,
    threat_type TEXT,
    source      TEXT,             -- AI_CLAUDE, AI_OPENAI, OFFLINE, BLOCKLIST, TLS
    cached_at   DATETIME DEFAULT CURRENT_TIMESTAMP,
    expires_at  DATETIME NOT NULL
);
CREATE INDEX idx_cache_expires ON security_cache(expires_at);

-- Configuración
CREATE TABLE settings (
    key         TEXT PRIMARY KEY,
    value       TEXT NOT NULL,
    modified_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Containers
CREATE TABLE containers (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    color       TEXT NOT NULL, -- Hex color
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Blocklist cache (para arranque sin internet)
CREATE TABLE blocklist_cache (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    domain      TEXT NOT NULL,
    source      TEXT NOT NULL,
    updated_at  DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_blocklist_domain ON blocklist_cache(domain);

-- Favicons cache
CREATE TABLE favicons (
    domain      TEXT PRIMARY KEY,
    data        BLOB NOT NULL,
    cached_at   DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

### 13.2 Settings Keys

```
security.mode                 = "Normal" | "Paranoid" | "Bunker"
privacy.fingerprint_level     = "Normal" | "Aggressive" | "Maximum"
privacy.webrtc_mode           = "Spoof" | "Disabled"
privacy.clear_on_exit         = "true" | "false"
privacy.history_enabled       = "true" | "false"
ai.mode                       = "Offline" | "Claude" | "Custom"
ai.api_key                    = "[encrypted]"
ai.custom_endpoint            = "http://..."
ai.claude_model                = "claude-sonnet-4-20250514" -- configurable en Settings → IA
dns.provider                  = "Quad9" | "NextDNS" | "Cloudflare" | "Custom"
dns.custom_url                = "https://..."
dns.nextdns_config_id         = "..."
search.engine                 = "DuckDuckGo" | "BraveSearch" | "SearxNG" | "Custom"
search.custom_url             = "https://...?q={query}"
cookiewall.consent_injector   = "true" | "false"
cookiewall.dom_extractor      = "true" | "false"
cookiewall.cache_fetcher      = "true" | "false"
vault.auto_lock_minutes       = "15"
vault.master_password_hash    = "[PBKDF2 hash]"
vault.salt                    = "[base64 salt]"
update.last_check             = "[ISO datetime]"
update.blocklists_last_update = "[ISO datetime]"
onboarding.completed          = "true" | "false"
```

---

## 14. CONFIGURACIÓN Y SETTINGS

### 14.1 Estructura de SettingsWindow

**Tab 1 — Privacidad:**
- Modo de seguridad (Normal / Paranoico / Bunker) — selector con descripción
- Nivel de fingerprint protection
- WebRTC: Spoof / Desactivado
- Limpiar datos al cerrar (auto si Paranoico/Bunker, manual en Normal)
- Historial: activar/desactivar, días a conservar (default: 30)
- Cookie Wall Bypass: activar cada estrategia individualmente

**Tab 2 — IA de Seguridad:**
- Selector de modo (Offline / Claude / Custom)
- Campo API key (si Claude)
- Campo endpoint + modelo (si Custom)
- Botón "Probar conexión"
- Link "¿Qué datos se envían a la IA?" → abre panel explicativo

**Tab 3 — DNS:**
- Selector de provider
- Campo config ID (si NextDNS)
- Campo URL custom
- Botón "Probar DNS"

**Tab 4 — Búsqueda:**
- Motor de búsqueda default
- Campo URL custom

**Tab 5 — Password Vault:**
- Botón "Cambiar master password"
- Auto-lock: slider de minutos
- Botón "Exportar vault (.velovault)"
- Botón "Importar vault"

**Tab 6 — Containers:**
- Lista de containers existentes
- Crear / editar / eliminar container
- Asignar color y nombre

**Tab 7 — Avanzado:**
- Blocklists: ver lista, forzar actualización, ver última actualización
- Caché de seguridad: ver tamaño, limpiar
- Logs: abrir carpeta de logs
- Exportar configuración completa
- Resetear a valores por defecto
- Desinstalar VELO completamente (borra todos los datos)

---

## 15. ONBOARDING WIZARD

Ver sección 12.1 para el flujo visual completo.

**Reglas del onboarding:**
- Si `onboarding.completed = false`: mostrar wizard al arrancar
- Una vez completado: nunca mostrar de nuevo (salvo reset manual en Settings → Avanzado)
- El wizard no se puede cerrar sin completarlo (no hay botón X)
- Si el usuario cierra la ventana principal durante el wizard: VELO se cierra
- La creación de master password es obligatoria y no se puede saltar
- La master password mínima: 8 caracteres. No hay política de complejidad forzada más allá de eso.

---

## 16. ROADMAP POR FASES

### Fase 1 — MVP Funcional (2-4 semanas)
**Objetivo:** Navegador que funciona y es más privado que Chrome desde el día 1.

- [ ] WPF MainWindow con WebView2 básico
- [ ] Tabs funcionales (TabBar + TabManager)
- [ ] UrlBar con navegación (atrás/adelante/reload/stop)
- [ ] NewTabPage minimal (100% local)
- [ ] RequestGuard con blocklists (EasyList + uBlock)
- [ ] Onboarding Wizard (3 pasos)
- [ ] Settings básico (modo IA + DoH)
- [ ] DoH con Quad9 por defecto
- [ ] WebView2 con zero-telemetría (flags hardcodeados)
- [ ] Search engine: DuckDuckGo por defecto
- [ ] AI Engine: OfflineAdapter + ClaudeAdapter
- [ ] SecurityPanel básico en UI

### Fase 2 — Seguridad Completa (4-6 semanas)
**Objetivo:** El navegador más seguro que puede usar alguien sin configurar nada.

- [ ] ScriptGuard completo con scoring
- [ ] TLSGuard (CT logs + HSTS + cert pinning)
- [ ] ContainerGuard + Container Tabs en UI
- [ ] Cookie Wall Bypass Engine (4 estrategias)
- [ ] Reader Mode
- [ ] Fingerprint protection (todos los niveles)
- [ ] WebRTC control (spoof + hard-off)
- [ ] Password Vault completo (AES-256 + UI)
- [ ] OpenAIAdapter + OllamaAdapter
- [ ] SecurityCache con SQLCipher
- [ ] Import desde Chrome/Firefox/Edge
- [ ] Clear on exit (Paranoico/Bunker)

### Fase 3 — Polish y Distribución (2-3 semanas)
**Objetivo:** Producto que un usuario no técnico puede instalar y usar.

- [ ] Settings completo (todos los tabs)
- [ ] DownloadManager (siempre pide ubicación)
- [ ] AutoUpdater (check en background, notificación no forzada)
- [ ] BlocklistUpdater (automático cada 7 días)
- [ ] velo://about page — contenido obligatorio: versión + commit hash (via GitVersionTask) + fecha de build + licencia AGPLv3 completa + tabla de dependencias con sus licencias (WebView2/BSD, SQLCipher/BSD-style, EasyList/CC-BY-SA, uBlock/GPLv3, etc.) + link a PRIVACY.md + link a SECURITY.md. Sin llamadas de red. Generada 100% localmente.
- [ ] Instalador MSIX
- [ ] README.md completo en GitHub
- [ ] PRIVACY.md ("qué se envía exactamente a la IA")
- [ ] SECURITY.md (cómo reportar vulnerabilidades)

### Fase 4 — Post-v2 (roadmap futuro, no comprometido)
- [ ] Stealth Mode — 4º nivel de seguridad (entre Paranoico y Bunker): fingerprint noise máximo + WebRTC off + Container forzado + DoH activo + JS activo (diferencia con Bunker donde JS está off)
- [ ] Soporte SOCKS5/HTTP proxy (para Tor o VPN personal)
- [ ] uBlock Origin embebido como extensión interna (solo él)
- [ ] Soporte básico de extensiones Manifest V3 (whitelist curada)
- [ ] App de Android (WebView-based, misma arquitectura)
- [ ] Sync cifrado entre dispositivos (E2E, sin servidor central — P2P)

---

## 17. DECISIONES DE DISEÑO INAMOVIBLES

Estas decisiones NO se pueden cambiar sin cambiar la filosofía del proyecto. Cualquier contribución que contradiga alguna de estas es automáticamente rechazada.

1. **Zero telemetría.** No hay opción para activarla. No hay toggle. No hay "optional analytics". Nunca.

2. **IA es opcional.** El navegador funciona 100% sin API key. El Modo Offline es ciudadano de primera clase, no un fallback de segunda.

3. **Sin extensiones en v1 y v2.** Nada que el usuario instale puede modificar el comportamiento de seguridad del navegador.

4. **Datos locales únicamente.** Historial, bookmarks, contraseñas, caché de seguridad — todo en el dispositivo del usuario, cifrado. Nunca en ningún servidor.

5. **El usuario decide cuándo actualizar.** AutoUpdater notifica, nunca instala solo.

6. **AGPLv3.** Cualquier fork tiene que ser igualmente open-source y transparente.

7. **Sin Bing. Sin Google como default.** Nunca.

8. **WebView2 zero-configuración de telemetría.** Los flags de desactivación van hardcodeados en el código, no en Settings.

9. **ConsentInjector acepta solo functional/necessary cookies.** Nunca analytics, nunca marketing, nunca advertising.

10. **Master password obligatoria para el vault.** No hay vault sin master password. No hay "continuar sin password".

---

## 18. NOTAS DE IMPLEMENTACIÓN POR MÓDULO

### Para quien implemente RequestGuard:
- WebResourceRequested se dispara en el thread de UI. Usar `async/await` correctamente para no bloquear.
- El evento debe responder en menos de 100ms o WebView2 lo ignora y deja pasar la request.
- Para BLOCK: usar `request.GetResponse()` con un response vacío de 403.
- Loggear todos los bloqueos a SQLite para que el usuario pueda verlos en SecurityPanel.

### Para quien implemente ScriptGuard:
- `AddScriptToExecuteOnDocumentCreatedAsync` se ejecuta antes de cualquier script de la página. Úsalo para inyectar el analizador.
- **Scripts externos — fire-and-forget + reactive blocking (OBLIGATORIO):**
  WebView2 tiene un timeout de ~100ms en `WebResourceRequested`. Si el handler tarda más, deja pasar la request de todas formas. Para scripts externos pesados, el único approach viable es:
  1. **Permitir que el script pase inmediatamente** (no bloquear el handler)
  2. **Analizar en background** (`Task.Run`) mientras el script se descarga/ejecuta
  3. **Si el análisis resulta BLOCK** → inyectar `window.stop()` via `ExecuteScriptAsync` + mostrar SecurityPanel + ofrecer recargar la página con ese script en blacklist
  
  ```csharp
  // ScriptGuard external = fire-and-forget + reactive blocking
  void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
  {
      if (IsExternalScript(e.Request))
      {
          // Dejar pasar inmediatamente — no bloquear el handler
          _ = Task.Run(async () =>
          {
              var verdict = await _scriptGuard.AnalyzeExternalAsync(e.Request.Uri);
              if (verdict.IsBlock)
              {
                  await Application.Current.Dispatcher.InvokeAsync(async () =>
                  {
                      await _webView.CoreWebView2.ExecuteScriptAsync("window.stop();");
                      _eventBus.Publish(new ScriptBlockedEvent(e.Request.Uri, verdict));
                  });
              }
          });
      }
  }
  ```
- **Scripts inline** → análisis síncrono (cabe en 100ms, no hay descarga de red).
- El análisis no puede bloquear el thread de UI. Todo en Task.Run o async.
- El snippet enviado a la IA NUNCA supera 500 caracteres. Truncar siempre.

### Para quien implemente TLSGuard:
- `ServerCertificateErrorDetected` solo se dispara cuando hay un error. Para validación CT proactiva, hacer el check en NavigationStarting.
- **CT logs check — non-blocking desde el día 1:**
  - El check de crt.sh se lanza en paralelo, NO bloquea NavigationStarting
  - Si cache está frío y crt.sh tarda > 800ms → WARN silencioso + continuar navegación (nunca BLOCK por timeout)
  - Solo BLOCK en casos obvios sin necesidad de crt.sh: self-signed en sitio público, TLS < 1.2, cipher suites deprecados
  - Cache de resultados CT: 24h por dominio (obligatorio — sin cache esto rompe la navegación)
  
  ```csharp
  // CT check paralelo — no bloquea NavigationStarting
  void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
  {
      // Checks síncronos rápidos primero (self-signed, TLS version)
      var quickVerdict = _tlsGuard.QuickCheck(e.Uri);
      if (quickVerdict.IsBlock) { e.Cancel = true; return; }
      
      // CT check en background — no bloquea la navegación
      _ = Task.Run(async () =>
      {
          using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
          var ctVerdict = await _tlsGuard.CheckCTLogsAsync(e.Uri, cts.Token);
          if (ctVerdict.IsWarn)
              _eventBus.Publish(new TLSWarnEvent(e.Uri, ctVerdict.Reason));
      });
  }
  ```
- Si crt.sh está caído: WARN al usuario, no BLOCK (principio de no romper navegación por fallo de infraestructura propia).

### Para quien implemente el Password Vault:
- SQLCipher se inicializa con la master password en el connection string: `Data Source=vault.db;Password=masterpassword`
- La master password NUNCA se almacena. Solo el hash PBKDF2. La key de SQLCipher se deriva de la master password en tiempo de ejecución.
- Auto-lock implementarlo con un DispatcherTimer que resetea en cada interacción del usuario con el vault.
- El archivo `.velovault` de export es un SQLite encriptado independiente, no el mismo que el de la app.

### Para quien implemente DoH:
- WebView2 no tiene hook nativo para interceptar DNS. Solución: configurar un proxy HTTP local en localhost que resuelva DoH antes de que el request salga.
- Alternativa más simple para MVP: usar la API de Windows para cambiar el DNS del proceso a un resolver DoH local. Investigar `DnsSetSystemSettings` o enfoque con HOSTS file dinámico.
- Para Fase 1: se puede hacer DoH "best effort" — si falla, caer a DNS del sistema con una advertencia. Mejorar en Fase 2.

### Para quien implemente CookieWall Bypass:
- DOMContentLoaded es el momento correcto para ejecutar PaywallDetector.
- ConsentInjector se ejecuta ANTES de DOMContentLoaded via AddScriptToExecuteOnDocumentCreatedAsync.
- CacheFetcher hace una request HTTP directa (HttpClient de C#), no a través de WebView2, para evitar que el sitio detecte que es el mismo navegador.
- El Reader Mode es una página local HTML que recibe el contenido extraído via PostWebMessageAsJson.

### Para quien implemente AutoUpdater:
- User-Agent del check de GitHub API: `VELO/{version} (Windows; +https://github.com/velo-browser/velo)`
- Frecuencia del check: al arrancar + cada 24 horas en background.
- Si el usuario elige "Recordar después": no volver a notificar en esa sesión.
- La URL de descarga va a la release de GitHub. VELO nunca descarga automáticamente.

---

*Fin de la documentación. Versión 2.2. Todo lo que no está aquí no está definido y debe consultarse antes de implementar.*
*Licencia del documento: AGPLv3 — mismo que el proyecto.*
