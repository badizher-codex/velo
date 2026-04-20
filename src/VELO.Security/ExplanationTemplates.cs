using VELO.Security.AI.Models;
using VELO.Security.Models;

namespace VELO.Security;

internal static class ExplanationTemplates
{
    internal record Template(
        string WhatHappened_es, string WhyBlocked_es, string WhatItMeans_es,
        string WhatHappened_en, string WhyBlocked_en, string WhatItMeans_en,
        string LearnMoreSlug);

    internal static readonly Dictionary<ThreatType, Template> ByThreatType = new()
    {
        [ThreatType.Tracker] = new(
            WhatHappened_es: "Este sitio intentó rastrearte entre páginas usando {source}.",
            WhyBlocked_es:   "Está en la lista {blocklistName} como tracker conocido. Confianza: {confidence}%.",
            WhatItMeans_es:  "Sin este bloqueo, {bigTechName} habría podido seguir tu actividad entre sitios.",
            WhatHappened_en: "This site attempted to track you across pages using {source}.",
            WhyBlocked_en:   "It is listed in {blocklistName} as a known tracker. Confidence: {confidence}%.",
            WhatItMeans_en:  "Without this block, {bigTechName} could have followed your activity across sites.",
            LearnMoreSlug:   "tracker"),

        [ThreatType.KnownTracker] = new(
            WhatHappened_es: "Se detectó un rastreador conocido: {source}.",
            WhyBlocked_es:   "Este dominio aparece en múltiples listas de bloqueo como rastreador verificado.",
            WhatItMeans_es:  "Tu identidad y hábitos de navegación habrían sido recopilados sin tu consentimiento.",
            WhatHappened_en: "A known tracker was detected: {source}.",
            WhyBlocked_en:   "This domain appears in multiple blocklists as a verified tracker.",
            WhatItMeans_en:  "Your identity and browsing habits would have been collected without your consent.",
            LearnMoreSlug:   "tracker"),

        [ThreatType.Miner] = new(
            WhatHappened_es: "Este sitio intentó usar la CPU de tu equipo para minar criptomonedas sin tu consentimiento.",
            WhyBlocked_es:   "Detectamos el patrón de minado (CoinHive, CryptoNight, o similar).",
            WhatItMeans_es:  "Sin este bloqueo, tu batería y factura eléctrica habrían subido mientras alguien se quedaba con el dinero.",
            WhatHappened_en: "This site attempted to use your CPU to mine cryptocurrency without your consent.",
            WhyBlocked_en:   "We detected a mining pattern (CoinHive, CryptoNight, or similar).",
            WhatItMeans_en:  "Without this block, your battery and electricity bill would have increased while someone else profited.",
            LearnMoreSlug:   "cryptominer"),

        [ThreatType.Malware] = new(
            WhatHappened_es: "Este sitio está clasificado como distribuidor de malware.",
            WhyBlocked_es:   "Aparece en la base de datos Malwaredex de VELO como sitio malicioso confirmado.",
            WhatItMeans_es:  "Visitar este sitio podría haber resultado en la instalación de software dañino en tu equipo.",
            WhatHappened_en: "This site is classified as a malware distributor.",
            WhyBlocked_en:   "It appears in VELO's Malwaredex database as a confirmed malicious site.",
            WhatItMeans_en:  "Visiting this site could have resulted in harmful software being installed on your device.",
            LearnMoreSlug:   "malware"),

        [ThreatType.Phishing] = new(
            WhatHappened_es: "Este sitio intenta suplantar la identidad de otro sitio legítimo para robar tus credenciales.",
            WhyBlocked_es:   "Identificamos indicadores de phishing: dominio similar a uno legítimo, formulario de login falso, o reporte activo.",
            WhatItMeans_es:  "Sin este bloqueo, podrías haber introducido tu contraseña en un sitio falso y perder el acceso a tu cuenta real.",
            WhatHappened_en: "This site is impersonating a legitimate site to steal your credentials.",
            WhyBlocked_en:   "We identified phishing indicators: similar domain to a legitimate site, fake login form, or active report.",
            WhatItMeans_en:  "Without this block, you could have entered your password on a fake site and lost access to your real account.",
            LearnMoreSlug:   "phishing"),

        [ThreatType.Fingerprinting] = new(
            WhatHappened_es: "Este sitio intentó crear un perfil único de tu navegador usando técnicas de fingerprinting.",
            WhyBlocked_es:   "Detectamos acceso a Canvas API, WebGL o AudioContext con el patrón típico de fingerprinting.",
            WhatItMeans_es:  "Sin protección, podrías ser identificado de forma única aunque borres cookies o uses modo privado.",
            WhatHappened_en: "This site attempted to create a unique profile of your browser using fingerprinting techniques.",
            WhyBlocked_en:   "We detected access to Canvas API, WebGL or AudioContext with a typical fingerprinting pattern.",
            WhatItMeans_en:  "Without protection, you could be uniquely identified even if you clear cookies or use private mode.",
            LearnMoreSlug:   "fingerprinting"),

        [ThreatType.MitM] = new(
            WhatHappened_es: "Detectamos un posible ataque de intermediario (Man-in-the-Middle) en la conexión.",
            WhyBlocked_es:   "El certificado TLS no pudo verificarse correctamente: {tlsError}.",
            WhatItMeans_es:  "Sin esta protección, un atacante podría leer o modificar todo lo que intercambias con este sitio.",
            WhatHappened_en: "We detected a potential Man-in-the-Middle attack on the connection.",
            WhyBlocked_en:   "The TLS certificate could not be verified correctly: {tlsError}.",
            WhatItMeans_en:  "Without this protection, an attacker could read or modify everything you exchange with this site.",
            LearnMoreSlug:   "mitm"),

        [ThreatType.DataExfiltration] = new(
            WhatHappened_es: "Este script intentó enviar datos de tu sesión a un servidor externo.",
            WhyBlocked_es:   "Detectamos una petición saliente sospechosa con datos que parecen ser de tu sesión o formulario.",
            WhatItMeans_es:  "Sin este bloqueo, información sensible podría haber salido de tu dispositivo sin que lo supieras.",
            WhatHappened_en: "This script attempted to send session data to an external server.",
            WhyBlocked_en:   "We detected a suspicious outbound request containing data that appears to be from your session or form.",
            WhatItMeans_en:  "Without this block, sensitive information could have left your device without your knowledge.",
            LearnMoreSlug:   "exfiltration"),

        [ThreatType.DnsRebinding] = new(
            WhatHappened_es: "Este sitio intentó un ataque de DNS rebinding para acceder a recursos en tu red local.",
            WhyBlocked_es:   "Detectamos una resolución DNS que apunta a una IP privada, patrón típico de este ataque.",
            WhatItMeans_es:  "Sin este bloqueo, el sitio podría haber accedido a tu router, impresora u otros dispositivos locales.",
            WhatHappened_en: "This site attempted a DNS rebinding attack to access resources on your local network.",
            WhyBlocked_en:   "We detected a DNS resolution pointing to a private IP, a typical pattern of this attack.",
            WhatItMeans_en:  "Without this block, the site could have accessed your router, printer, or other local devices.",
            LearnMoreSlug:   "dns-rebinding"),

        [ThreatType.SSRF] = new(
            WhatHappened_es: "Se detectó un intento de falsificación de petición del lado del servidor (SSRF).",
            WhyBlocked_es:   "La petición estaba dirigida a recursos internos que no deberían ser accesibles desde el exterior.",
            WhatItMeans_es:  "Este tipo de ataque puede exponer servicios internos de una red o servidor.",
            WhatHappened_en: "A Server-Side Request Forgery (SSRF) attempt was detected.",
            WhyBlocked_en:   "The request was directed at internal resources that should not be externally accessible.",
            WhatItMeans_en:  "This type of attack can expose internal services of a network or server.",
            LearnMoreSlug:   "ssrf"),

        [ThreatType.MixedContent] = new(
            WhatHappened_es: "Esta página HTTPS intenta cargar recursos por HTTP sin cifrar.",
            WhyBlocked_es:   "El contenido mixto debilita la seguridad de la conexión cifrada.",
            WhatItMeans_es:  "Un recurso sin cifrar en una página segura puede ser interceptado y modificado por un atacante.",
            WhatHappened_en: "This HTTPS page is attempting to load resources over unencrypted HTTP.",
            WhyBlocked_en:   "Mixed content weakens the security of the encrypted connection.",
            WhatItMeans_en:  "An unencrypted resource on a secure page can be intercepted and modified by an attacker.",
            LearnMoreSlug:   "mixed-content"),

        [ThreatType.ContainerViolation] = new(
            WhatHappened_es: "Este sitio intentó acceder a datos de otro container.",
            WhyBlocked_es:   "Los containers de VELO están aislados: cada uno tiene sus propias cookies, almacenamiento y caché.",
            WhatItMeans_es:  "Sin este aislamiento, un sitio podría acceder a sesiones de otros contextos (trabajo, banca, personal).",
            WhatHappened_en: "This site attempted to access data from another container.",
            WhyBlocked_en:   "VELO containers are isolated: each has its own cookies, storage, and cache.",
            WhatItMeans_en:  "Without this isolation, a site could access sessions from other contexts (work, banking, personal).",
            LearnMoreSlug:   "container-isolation"),

        [ThreatType.Other] = new(
            WhatHappened_es: "VELO detectó una actividad sospechosa en este sitio.",
            WhyBlocked_es:   "El análisis de seguridad identificó un patrón de riesgo que no coincide con categorías conocidas.",
            WhatItMeans_es:  "Como medida de precaución, VELO bloqueó esta acción para proteger tu privacidad.",
            WhatHappened_en: "VELO detected suspicious activity on this site.",
            WhyBlocked_en:   "The security analysis identified a risk pattern that doesn't match known categories.",
            WhatItMeans_en:  "As a precaution, VELO blocked this action to protect your privacy.",
            LearnMoreSlug:   "other"),

        [ThreatType.None] = new(
            WhatHappened_es: "No se detectaron amenazas en este sitio.",
            WhyBlocked_es:   "El análisis de seguridad no encontró indicadores de riesgo.",
            WhatItMeans_es:  "Este sitio parece seguro según los criterios actuales de VELO.",
            WhatHappened_en: "No threats were detected on this site.",
            WhyBlocked_en:   "The security analysis found no risk indicators.",
            WhatItMeans_en:  "This site appears safe according to VELO's current criteria.",
            LearnMoreSlug:   "safe"),
    };

    // TLS-specific explanations keyed by error description
    internal static readonly Dictionary<string, Template> ByTlsError = new()
    {
        ["expired"] = new(
            WhatHappened_es: "El certificado de seguridad de este sitio ha expirado.",
            WhyBlocked_es:   "Un certificado expirado significa que la identidad del servidor no puede verificarse correctamente.",
            WhatItMeans_es:  "Podrías estar conectando a un servidor diferente al que crees, o el sitio simplemente olvidó renovar su certificado.",
            WhatHappened_en: "This site's security certificate has expired.",
            WhyBlocked_en:   "An expired certificate means the server's identity cannot be properly verified.",
            WhatItMeans_en:  "You could be connecting to a different server than you think, or the site simply forgot to renew its certificate.",
            LearnMoreSlug:   "tls-expired"),

        ["self-signed"] = new(
            WhatHappened_es: "Este sitio usa un certificado autofirmado, no emitido por una autoridad reconocida.",
            WhyBlocked_es:   "Los certificados autofirmados no están verificados por ninguna CA pública, por lo que no garantizan la identidad del servidor.",
            WhatItMeans_es:  "En sitios públicos, esto es inusual y puede indicar una configuración incorrecta o un intento de interceptar tu tráfico.",
            WhatHappened_en: "This site uses a self-signed certificate, not issued by a recognized authority.",
            WhyBlocked_en:   "Self-signed certificates are not verified by any public CA, so they don't guarantee the server's identity.",
            WhatItMeans_en:  "On public sites, this is unusual and may indicate a misconfiguration or an attempt to intercept your traffic.",
            LearnMoreSlug:   "tls-self-signed"),

        ["no-ct"] = new(
            WhatHappened_es: "Este dominio no tiene entradas en los logs de Certificate Transparency públicos.",
            WhyBlocked_es:   "Los CT logs son un sistema de auditoría global que registra todos los certificados legítimos. Su ausencia es inusual.",
            WhatItMeans_es:  "Podría indicar un certificado emitido por una CA privada o no estándar, o un intento de interceptar la conexión.",
            WhatHappened_en: "This domain has no entries in public Certificate Transparency logs.",
            WhyBlocked_en:   "CT logs are a global audit system that records all legitimate certificates. Their absence is unusual.",
            WhatItMeans_en:  "This could indicate a certificate issued by a private or non-standard CA, or an attempt to intercept the connection.",
            LearnMoreSlug:   "ct-logs"),

        ["http"] = new(
            WhatHappened_es: "Estás accediendo a este sitio sin cifrado (HTTP, no HTTPS).",
            WhyBlocked_es:   "HTTP transmite todos los datos en texto plano, visible para cualquiera en la misma red.",
            WhatItMeans_es:  "Tu ISP, el administrador de WiFi, o cualquier observador de red puede ver exactamente qué estás viendo y enviando.",
            WhatHappened_en: "You are accessing this site without encryption (HTTP, not HTTPS).",
            WhyBlocked_en:   "HTTP transmits all data in plain text, visible to anyone on the same network.",
            WhatItMeans_en:  "Your ISP, WiFi administrator, or any network observer can see exactly what you are viewing and sending.",
            LearnMoreSlug:   "http-insecure"),
    };

    // Script-analysis explanations
    internal static Template GetScriptTemplate(int riskScore) => riskScore switch
    {
        >= 80 => new(
            WhatHappened_es: "Un script en esta página fue bloqueado por comportamiento muy sospechoso (score: {score}).",
            WhyBlocked_es:   "El análisis detectó patrones de alto riesgo: {patterns}.",
            WhatItMeans_es:  "Este script podría haber recopilado datos de tu sesión, rastreado tu comportamiento o ejecutado código malicioso.",
            WhatHappened_en: "A script on this page was blocked for highly suspicious behavior (score: {score}).",
            WhyBlocked_en:   "The analysis detected high-risk patterns: {patterns}.",
            WhatItMeans_en:  "This script could have collected session data, tracked your behavior, or executed malicious code.",
            LearnMoreSlug:   "script-block"),
        >= 40 => new(
            WhatHappened_es: "Un script en esta página muestra comportamiento sospechoso (score: {score}).",
            WhyBlocked_es:   "Detectamos patrones que pueden indicar rastreo o actividad invasiva: {patterns}.",
            WhatItMeans_es:  "El script no fue bloqueado pero está siendo monitorizado. Si el comportamiento aumenta, se bloqueará.",
            WhatHappened_en: "A script on this page shows suspicious behavior (score: {score}).",
            WhyBlocked_en:   "We detected patterns that may indicate tracking or invasive activity: {patterns}.",
            WhatItMeans_en:  "The script was not blocked but is being monitored. If the behavior increases, it will be blocked.",
            LearnMoreSlug:   "script-warn"),
        _ => new(
            WhatHappened_es: "Un script en esta página fue analizado y resultó seguro (score: {score}).",
            WhyBlocked_es:   "No se detectaron patrones de riesgo en este script.",
            WhatItMeans_es:  "Este script parece legítimo y no presenta indicadores de comportamiento invasivo.",
            WhatHappened_en: "A script on this page was analyzed and found to be safe (score: {score}).",
            WhyBlocked_en:   "No risk patterns were detected in this script.",
            WhatItMeans_en:  "This script appears legitimate and shows no indicators of invasive behavior.",
            LearnMoreSlug:   "script-safe"),
    };
}
