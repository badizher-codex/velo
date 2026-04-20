namespace VELO.Core.Localization;

public sealed class LocalizationService
{
    public static LocalizationService Current { get; } = new();

    public event Action? LanguageChanged;

    private string _lang = "es";
    public string Language => _lang;

    // All supported languages: code -> native name
    public static readonly IReadOnlyDictionary<string, string> Languages =
        new Dictionary<string, string>
        {
            ["es"] = "Español",
            ["en"] = "English",
            ["pt"] = "Português",
            ["fr"] = "Français",
            ["de"] = "Deutsch",
            ["zh"] = "中文",
            ["ru"] = "Русский",
            ["ja"] = "日本語",
        };

    public void SetLanguage(string lang)
    {
        if (!Languages.ContainsKey(lang) || _lang == lang) return;
        _lang = lang;
        LanguageChanged?.Invoke();
    }

    public string T(string key)
    {
        if (_strings.TryGetValue(key, out var langs) &&
            langs.TryGetValue(_lang, out var val))
            return val;
        // Fallback chain: current lang → es → key itself
        if (_strings.TryGetValue(key, out langs) &&
            langs.TryGetValue("es", out val))
            return val;
        return key;
    }

    // ── String table ────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Dictionary<string, string>> _strings = new()
    {
        // ── Menu ──────────────────────────────────────────────────────────
        ["menu.settings"] = new()
        {
            ["es"] = "⚙  Configuración",
            ["en"] = "⚙  Settings",
            ["pt"] = "⚙  Configurações",
            ["fr"] = "⚙  Paramètres",
            ["de"] = "⚙  Einstellungen",
            ["zh"] = "⚙  设置",
            ["ru"] = "⚙  Настройки",
            ["ja"] = "⚙  設定",
        },
        ["menu.vault"] = new()
        {
            ["es"] = "🔑  Password Vault",
            ["en"] = "🔑  Password Vault",
            ["pt"] = "🔑  Cofre de Senhas",
            ["fr"] = "🔑  Coffre-fort",
            ["de"] = "🔑  Passwort-Tresor",
            ["zh"] = "🔑  密码库",
            ["ru"] = "🔑  Хранилище паролей",
            ["ja"] = "🔑  パスワード保管庫",
        },
        ["menu.bookmarks"] = new()
        {
            ["es"] = "★  Marcadores",
            ["en"] = "★  Bookmarks",
            ["pt"] = "★  Favoritos",
            ["fr"] = "★  Favoris",
            ["de"] = "★  Lesezeichen",
            ["zh"] = "★  书签",
            ["ru"] = "★  Закладки",
            ["ja"] = "★  ブックマーク",
        },
        ["menu.history"] = new()
        {
            ["es"] = "🕐  Historial",
            ["en"] = "🕐  History",
            ["pt"] = "🕐  Histórico",
            ["fr"] = "🕐  Historique",
            ["de"] = "🕐  Verlauf",
            ["zh"] = "🕐  历史记录",
            ["ru"] = "🕐  История",
            ["ja"] = "🕐  履歴",
        },
        ["menu.downloads"] = new()
        {
            ["es"] = "⬇  Descargas",
            ["en"] = "⬇  Downloads",
            ["pt"] = "⬇  Downloads",
            ["fr"] = "⬇  Téléchargements",
            ["de"] = "⬇  Downloads",
            ["zh"] = "⬇  下载",
            ["ru"] = "⬇  Загрузки",
            ["ja"] = "⬇  ダウンロード",
        },
        ["menu.malwaredex"] = new()
        {
            ["es"] = "👾  Malwaredex",
            ["en"] = "👾  Malwaredex",
            ["pt"] = "👾  Malwaredex",
            ["fr"] = "👾  Malwaredex",
            ["de"] = "👾  Malwaredex",
            ["zh"] = "👾  恶意软件图鉴",
            ["ru"] = "👾  Malwaredex",
            ["ja"] = "👾  マルウェア図鑑",
        },
        ["menu.cleardata"] = new()
        {
            ["es"] = "🗑  Limpiar datos de navegación…",
            ["en"] = "🗑  Clear browsing data…",
            ["pt"] = "🗑  Limpar dados de navegação…",
            ["fr"] = "🗑  Effacer les données…",
            ["de"] = "🗑  Browserdaten löschen…",
            ["zh"] = "🗑  清除浏览数据…",
            ["ru"] = "🗑  Очистить данные…",
            ["ja"] = "🗑  閲覧データを消去…",
        },
        ["menu.about"] = new()
        {
            ["es"] = "ℹ️  Acerca de VELO",
            ["en"] = "ℹ️  About VELO",
            ["pt"] = "ℹ️  Sobre o VELO",
            ["fr"] = "ℹ️  À propos de VELO",
            ["de"] = "ℹ️  Über VELO",
            ["zh"] = "ℹ️  关于 VELO",
            ["ru"] = "ℹ️  О программе VELO",
            ["ja"] = "ℹ️  VELOについて",
        },

        // ── Settings sidebar nav ──────────────────────────────────────────
        ["nav.privacy"] = new()
        {
            ["es"] = "🔒  Privacidad",
            ["en"] = "🔒  Privacy",
            ["pt"] = "🔒  Privacidade",
            ["fr"] = "🔒  Confidentialité",
            ["de"] = "🔒  Datenschutz",
            ["zh"] = "🔒  隐私",
            ["ru"] = "🔒  Конфиденциальность",
            ["ja"] = "🔒  プライバシー",
        },
        ["nav.ai"] = new()
        {
            ["es"] = "🧠  Inteligencia Artificial",
            ["en"] = "🧠  Artificial Intelligence",
            ["pt"] = "🧠  Inteligência Artificial",
            ["fr"] = "🧠  Intelligence Artificielle",
            ["de"] = "🧠  Künstliche Intelligenz",
            ["zh"] = "🧠  人工智能",
            ["ru"] = "🧠  Искусственный интеллект",
            ["ja"] = "🧠  人工知能",
        },
        ["nav.search"] = new()
        {
            ["es"] = "🔍  Búsqueda",
            ["en"] = "🔍  Search",
            ["pt"] = "🔍  Pesquisa",
            ["fr"] = "🔍  Recherche",
            ["de"] = "🔍  Suche",
            ["zh"] = "🔍  搜索",
            ["ru"] = "🔍  Поиск",
            ["ja"] = "🔍  検索",
        },
        ["nav.language"] = new()
        {
            ["es"] = "🌍  Idioma",
            ["en"] = "🌍  Language",
            ["pt"] = "🌍  Idioma",
            ["fr"] = "🌍  Langue",
            ["de"] = "🌍  Sprache",
            ["zh"] = "🌍  语言",
            ["ru"] = "🌍  Язык",
            ["ja"] = "🌍  言語",
        },

        // ── Clear Data dialog ─────────────────────────────────────────────
        ["cleardata.title"] = new()
        {
            ["es"] = "Limpiar datos de navegación",
            ["en"] = "Clear browsing data",
            ["pt"] = "Limpar dados de navegação",
            ["fr"] = "Effacer les données de navigation",
            ["de"] = "Browserdaten löschen",
            ["zh"] = "清除浏览数据",
            ["ru"] = "Очистить данные браузера",
            ["ja"] = "閲覧データを消去",
        },
        ["cleardata.subtitle"] = new()
        {
            ["es"] = "Selecciona qué datos deseas eliminar:",
            ["en"] = "Select what data you want to delete:",
            ["pt"] = "Selecione os dados que deseja excluir:",
            ["fr"] = "Sélectionnez les données à supprimer :",
            ["de"] = "Wähle aus, welche Daten gelöscht werden sollen:",
            ["zh"] = "选择要删除的数据：",
            ["ru"] = "Выберите данные для удаления:",
            ["ja"] = "削除するデータを選択してください：",
        },
        ["cleardata.history"] = new()
        {
            ["es"] = "Historial de navegación",
            ["en"] = "Browsing history",
            ["pt"] = "Histórico de navegação",
            ["fr"] = "Historique de navigation",
            ["de"] = "Browserverlauf",
            ["zh"] = "浏览历史",
            ["ru"] = "История браузера",
            ["ja"] = "閲覧履歴",
        },
        ["cleardata.history.desc"] = new()
        {
            ["es"] = "Páginas visitadas y búsquedas recientes",
            ["en"] = "Pages visited and recent searches",
            ["pt"] = "Páginas visitadas e pesquisas recentes",
            ["fr"] = "Pages visitées et recherches récentes",
            ["de"] = "Besuchte Seiten und letzte Suchen",
            ["zh"] = "访问的页面和最近搜索",
            ["ru"] = "Посещённые страницы и недавние поиски",
            ["ja"] = "閲覧したページと最近の検索",
        },
        ["cleardata.cookies"] = new()
        {
            ["es"] = "Cookies y datos de sesión",
            ["en"] = "Cookies and session data",
            ["pt"] = "Cookies e dados de sessão",
            ["fr"] = "Cookies et données de session",
            ["de"] = "Cookies und Sitzungsdaten",
            ["zh"] = "Cookie 和会话数据",
            ["ru"] = "Файлы cookie и данные сессий",
            ["ja"] = "Cookie とセッションデータ",
        },
        ["cleardata.cookies.desc"] = new()
        {
            ["es"] = "Cierra sesión en todos los sitios",
            ["en"] = "Logs you out of all sites",
            ["pt"] = "Desconecta de todos os sites",
            ["fr"] = "Vous déconnecte de tous les sites",
            ["de"] = "Meldet dich von allen Seiten ab",
            ["zh"] = "退出所有网站登录",
            ["ru"] = "Выход из всех сайтов",
            ["ja"] = "すべてのサイトからログアウト",
        },
        ["cleardata.cache"] = new()
        {
            ["es"] = "Caché de imágenes y archivos",
            ["en"] = "Cached images and files",
            ["pt"] = "Cache de imagens e arquivos",
            ["fr"] = "Images et fichiers en cache",
            ["de"] = "Bilder und Dateien im Cache",
            ["zh"] = "缓存的图片和文件",
            ["ru"] = "Кэшированные изображения и файлы",
            ["ja"] = "キャッシュされた画像とファイル",
        },
        ["cleardata.cache.desc"] = new()
        {
            ["es"] = "Libera espacio en disco",
            ["en"] = "Frees up disk space",
            ["pt"] = "Libera espaço em disco",
            ["fr"] = "Libère de l'espace disque",
            ["de"] = "Gibt Speicherplatz frei",
            ["zh"] = "释放磁盘空间",
            ["ru"] = "Освобождает место на диске",
            ["ja"] = "ディスク容量を解放",
        },
        ["cleardata.downloads"] = new()
        {
            ["es"] = "Lista de descargas",
            ["en"] = "Downloads list",
            ["pt"] = "Lista de downloads",
            ["fr"] = "Liste de téléchargements",
            ["de"] = "Download-Liste",
            ["zh"] = "下载列表",
            ["ru"] = "Список загрузок",
            ["ja"] = "ダウンロードリスト",
        },
        ["cleardata.downloads.desc"] = new()
        {
            ["es"] = "Solo elimina el registro, no los archivos descargados",
            ["en"] = "Only removes the record, not the downloaded files",
            ["pt"] = "Remove apenas o registro, não os arquivos baixados",
            ["fr"] = "Supprime uniquement la liste, pas les fichiers",
            ["de"] = "Löscht nur den Eintrag, nicht die Dateien",
            ["zh"] = "仅删除记录，不删除已下载的文件",
            ["ru"] = "Удаляет только запись, не сами файлы",
            ["ja"] = "記録のみ削除、ファイルは保持",
        },
        ["cleardata.clearing"] = new()
        {
            ["es"] = "Limpiando…",
            ["en"] = "Clearing…",
            ["pt"] = "Limpando…",
            ["fr"] = "Suppression…",
            ["de"] = "Wird gelöscht…",
            ["zh"] = "正在清除…",
            ["ru"] = "Очистка…",
            ["ja"] = "消去中…",
        },
        ["cleardata.done"] = new()
        {
            ["es"] = "✓ Datos eliminados correctamente.",
            ["en"] = "✓ Data cleared successfully.",
            ["pt"] = "✓ Dados apagados com sucesso.",
            ["fr"] = "✓ Données supprimées avec succès.",
            ["de"] = "✓ Daten erfolgreich gelöscht.",
            ["zh"] = "✓ 数据已成功清除。",
            ["ru"] = "✓ Данные успешно удалены.",
            ["ja"] = "✓ データを正常に消去しました。",
        },
        ["cleardata.cancel"] = new()
        {
            ["es"] = "Cancelar",
            ["en"] = "Cancel",
            ["pt"] = "Cancelar",
            ["fr"] = "Annuler",
            ["de"] = "Abbrechen",
            ["zh"] = "取消",
            ["ru"] = "Отмена",
            ["ja"] = "キャンセル",
        },
        ["cleardata.confirm"] = new()
        {
            ["es"] = "Limpiar ahora",
            ["en"] = "Clear now",
            ["pt"] = "Limpar agora",
            ["fr"] = "Effacer maintenant",
            ["de"] = "Jetzt löschen",
            ["zh"] = "立即清除",
            ["ru"] = "Очистить сейчас",
            ["ja"] = "今すぐ消去",
        },

        // ── Language panel ────────────────────────────────────────────────
        ["lang.title"] = new()
        {
            ["es"] = "Idioma",
            ["en"] = "Language",
            ["pt"] = "Idioma",
            ["fr"] = "Langue",
            ["de"] = "Sprache",
            ["zh"] = "语言",
            ["ru"] = "Язык",
            ["ja"] = "言語",
        },
        ["lang.subtitle"] = new()
        {
            ["es"] = "Selecciona el idioma de la interfaz. Se aplica al instante.",
            ["en"] = "Select the interface language. Applied instantly.",
            ["pt"] = "Selecione o idioma da interface. Aplicado instantaneamente.",
            ["fr"] = "Sélectionnez la langue de l'interface. Appliqué immédiatement.",
            ["de"] = "Wähle die Sprache der Oberfläche. Sofort angewendet.",
            ["zh"] = "选择界面语言，立即生效。",
            ["ru"] = "Выберите язык интерфейса. Применяется мгновенно.",
            ["ja"] = "インターフェースの言語を選択。即時反映されます。",
        },
        ["lang.choose"] = new()
        {
            ["es"] = "Idioma de la interfaz",
            ["en"] = "Interface language",
            ["pt"] = "Idioma da interface",
            ["fr"] = "Langue de l'interface",
            ["de"] = "Oberflächensprache",
            ["zh"] = "界面语言",
            ["ru"] = "Язык интерфейса",
            ["ja"] = "インターフェース言語",
        },

        // ── History ───────────────────────────────────────────────────────
        ["history.title"] = new() { ["es"]="Historial",["en"]="History",["pt"]="Histórico",["fr"]="Historique",["de"]="Verlauf",["zh"]="历史记录",["ru"]="История",["ja"]="履歴" },
        ["history.search"] = new() { ["es"]="Buscar en historial…",["en"]="Search history…",["pt"]="Pesquisar histórico…",["fr"]="Rechercher…",["de"]="Verlauf durchsuchen…",["zh"]="搜索历史…",["ru"]="Поиск в истории…",["ja"]="履歴を検索…" },
        ["history.clearall"] = new() { ["es"]="Borrar todo",["en"]="Clear all",["pt"]="Apagar tudo",["fr"]="Tout effacer",["de"]="Alles löschen",["zh"]="全部删除",["ru"]="Очистить всё",["ja"]="すべて消去" },
        ["history.confirm.clear"] = new() { ["es"]="¿Borrar todo el historial?",["en"]="Clear all history?",["pt"]="Apagar todo o histórico?",["fr"]="Effacer tout l'historique ?",["de"]="Gesamten Verlauf löschen?",["zh"]="清除所有历史记录？",["ru"]="Очистить всю историю?",["ja"]="すべての履歴を消去しますか？" },
        ["history.entries"] = new() { ["es"]="entradas",["en"]="entries",["pt"]="entradas",["fr"]="entrées",["de"]="Einträge",["zh"]="条记录",["ru"]="записей",["ja"]="件" },
        ["history.entry"] = new() { ["es"]="entrada",["en"]="entry",["pt"]="entrada",["fr"]="entrée",["de"]="Eintrag",["zh"]="条记录",["ru"]="запись",["ja"]="件" },
        ["history.no.threats"] = new() { ["es"]="✓ Sin amenazas",["en"]="✓ No threats",["pt"]="✓ Sem ameaças",["fr"]="✓ Sans menace",["de"]="✓ Keine Bedrohungen",["zh"]="✓ 无威胁",["ru"]="✓ Угроз нет",["ja"]="✓ 脅威なし" },
        ["history.blocked"] = new() { ["es"]=" bloqueados",["en"]=" blocked",["pt"]=" bloqueados",["fr"]=" bloqués",["de"]=" blockiert",["zh"]=" 已拦截",["ru"]=" заблок.",["ja"]=" ブロック" },
        ["history.trackers"] = new() { ["es"]=" rastreadores",["en"]=" trackers",["pt"]=" rastreadores",["fr"]=" traceurs",["de"]=" Tracker",["zh"]=" 跟踪器",["ru"]=" трекеров",["ja"]=" トラッカー" },
        ["history.malware"] = new() { ["es"]=" malware",["en"]=" malware",["pt"]=" malware",["fr"]=" malware",["de"]=" Malware",["zh"]=" 恶意软件",["ru"]=" вредон.",["ja"]=" マルウェア" },

        // ── Bookmarks ──────────────────────────────────────────────────────
        ["bookmarks.title"] = new() { ["es"]="★ Marcadores",["en"]="★ Bookmarks",["pt"]="★ Favoritos",["fr"]="★ Favoris",["de"]="★ Lesezeichen",["zh"]="★ 书签",["ru"]="★ Закладки",["ja"]="★ ブックマーク" },
        ["bookmarks.search"] = new() { ["es"]="Buscar marcadores…",["en"]="Search bookmarks…",["pt"]="Pesquisar favoritos…",["fr"]="Rechercher…",["de"]="Lesezeichen suchen…",["zh"]="搜索书签…",["ru"]="Поиск закладок…",["ja"]="ブックマーク検索…" },
        ["bookmarks.empty"] = new() { ["es"]="Sin marcadores todavía",["en"]="No bookmarks yet",["pt"]="Sem favoritos ainda",["fr"]="Aucun favori",["de"]="Noch keine Lesezeichen",["zh"]="暂无书签",["ru"]="Закладок пока нет",["ja"]="ブックマークなし" },
        ["bookmarks.delete"] = new() { ["es"]="Eliminar marcador",["en"]="Delete bookmark",["pt"]="Excluir favorito",["fr"]="Supprimer le favori",["de"]="Lesezeichen löschen",["zh"]="删除书签",["ru"]="Удалить закладку",["ja"]="削除" },

        // ── Downloads ──────────────────────────────────────────────────────
        ["downloads.title"] = new() { ["es"]="Descargas",["en"]="Downloads",["pt"]="Downloads",["fr"]="Téléchargements",["de"]="Downloads",["zh"]="下载",["ru"]="Загрузки",["ja"]="ダウンロード" },
        ["downloads.clear"] = new() { ["es"]="Limpiar completadas",["en"]="Clear completed",["pt"]="Limpar concluídos",["fr"]="Effacer terminés",["de"]="Abgeschlossene löschen",["zh"]="清除已完成",["ru"]="Очистить завершённые",["ja"]="完了済みを消去" },
        ["downloads.open.folder"] = new() { ["es"]="Abrir carpeta",["en"]="Open folder",["pt"]="Abrir pasta",["fr"]="Ouvrir le dossier",["de"]="Ordner öffnen",["zh"]="打开文件夹",["ru"]="Открыть папку",["ja"]="フォルダを開く" },
        ["downloads.remove"] = new() { ["es"]="Quitar de la lista",["en"]="Remove from list",["pt"]="Remover da lista",["fr"]="Retirer de la liste",["de"]="Aus Liste entfernen",["zh"]="从列表移除",["ru"]="Убрать из списка",["ja"]="リストから削除" },
        ["downloads.files"] = new() { ["es"]="archivos",["en"]="files",["pt"]="arquivos",["fr"]="fichiers",["de"]="Dateien",["zh"]="个文件",["ru"]="файлов",["ja"]="ファイル" },
        ["downloads.file"] = new() { ["es"]="archivo",["en"]="file",["pt"]="arquivo",["fr"]="fichier",["de"]="Datei",["zh"]="个文件",["ru"]="файл",["ja"]="ファイル" },
        ["downloads.inprogress"] = new() { ["es"]="en curso",["en"]="in progress",["pt"]="em andamento",["fr"]="en cours",["de"]="läuft",["zh"]="进行中",["ru"]="загружается",["ja"]="ダウンロード中" },

        // ── Malwaredex ─────────────────────────────────────────────────────
        ["malwaredex.loading"] = new() { ["es"]="Cargando…",["en"]="Loading…",["pt"]="Carregando…",["fr"]="Chargement…",["de"]="Lädt…",["zh"]="加载中…",["ru"]="Загрузка…",["ja"]="読込中…" },
        ["malwaredex.empty.title"] = new() { ["es"]="Ninguna amenaza capturada todavía",["en"]="No threats captured yet",["pt"]="Nenhuma ameaça capturada ainda",["fr"]="Aucune menace capturée",["de"]="Noch keine Bedrohungen",["zh"]="尚未捕获任何威胁",["ru"]="Угроз пока не обнаружено",["ja"]="まだ脅威は検出されていません" },
        ["malwaredex.empty.desc"] = new() { ["es"]="Navega en la web y VELO registrará aquí cada tipo de amenaza bloqueada.",["en"]="Browse the web and VELO will log every blocked threat type here.",["pt"]="Navegue na web e o VELO registrará cada tipo de ameaça bloqueada.",["fr"]="Naviguez et VELO enregistrera chaque menace bloquée ici.",["de"]="Surfe im Web und VELO protokolliert jede blockierte Bedrohung hier.",["zh"]="浏览网页，VELO 将在此记录每种被拦截的威胁。",["ru"]="Открывайте сайты, и VELO будет записывать каждую заблокированную угрозу.",["ja"]="ウェブを閲覧すると、VELOがブロックした脅威をここに記録します。" },
        ["malwaredex.subtitle"] = new() { ["es"]="amenazas capturadas en",["en"]="threats captured across",["pt"]="ameaças capturadas em",["fr"]="menaces capturées dans",["de"]="Bedrohungen in",["zh"]="个威胁，分布在",["ru"]="угроз в",["ja"]="件の脅威、カテゴリ" },
        ["malwaredex.categories"] = new() { ["es"]="categorías",["en"]="categories",["pt"]="categorias",["fr"]="catégories",["de"]="Kategorien",["zh"]="个类别",["ru"]="категориях",["ja"]="カテゴリ" },

        // ── Vault ──────────────────────────────────────────────────────────
        ["vault.unlock.subtitle"] = new() { ["es"]="Ingresa tu master password para continuar.",["en"]="Enter your master password to continue.",["pt"]="Digite sua senha mestra para continuar.",["fr"]="Entrez votre mot de passe maître.",["de"]="Master-Passwort eingeben.",["zh"]="输入主密码以继续。",["ru"]="Введите мастер-пароль.",["ja"]="マスターパスワードを入力してください。" },
        ["vault.unlock.btn"] = new() { ["es"]="Desbloquear",["en"]="Unlock",["pt"]="Desbloquear",["fr"]="Déverrouiller",["de"]="Entsperren",["zh"]="解锁",["ru"]="Разблокировать",["ja"]="ロック解除" },
        ["vault.master.label"] = new() { ["es"]="Master password:",["en"]="Master password:",["pt"]="Senha mestra:",["fr"]="Mot de passe maître :",["de"]="Master-Passwort:",["zh"]="主密码：",["ru"]="Мастер-пароль:",["ja"]="マスターパスワード：" },
        ["vault.wrong.password"] = new() { ["es"]="Contraseña incorrecta.",["en"]="Wrong password.",["pt"]="Senha incorreta.",["fr"]="Mot de passe incorrect.",["de"]="Falsches Passwort.",["zh"]="密码错误。",["ru"]="Неверный пароль.",["ja"]="パスワードが違います。" },

        // ── UrlBar tooltips ───────────────────────────────────────────────
        ["nav.back"]     = new() { ["es"]="Atrás",["en"]="Back",["pt"]="Voltar",["fr"]="Retour",["de"]="Zurück",["zh"]="后退",["ru"]="Назад",["ja"]="戻る" },
        ["nav.forward"]  = new() { ["es"]="Adelante",["en"]="Forward",["pt"]="Avançar",["fr"]="Suivant",["de"]="Vorwärts",["zh"]="前进",["ru"]="Вперёд",["ja"]="進む" },
        ["nav.reload"]   = new() { ["es"]="Recargar",["en"]="Reload",["pt"]="Recarregar",["fr"]="Actualiser",["de"]="Neu laden",["zh"]="刷新",["ru"]="Обновить",["ja"]="再読み込み" },
        ["nav.bookmark"] = new() { ["es"]="Guardar marcador",["en"]="Save bookmark",["pt"]="Salvar favorito",["fr"]="Enregistrer favori",["de"]="Lesezeichen speichern",["zh"]="保存书签",["ru"]="Сохранить закладку",["ja"]="ブックマーク保存" },
        ["nav.reader"]   = new() { ["es"]="Modo Lector (F9)",["en"]="Reader Mode (F9)",["pt"]="Modo Leitura (F9)",["fr"]="Mode Lecture (F9)",["de"]="Lesemodus (F9)",["zh"]="阅读模式 (F9)",["ru"]="Режим чтения (F9)",["ja"]="リーダーモード (F9)" },
        ["nav.menu"]     = new() { ["es"]="Menú",["en"]="Menu",["pt"]="Menu",["fr"]="Menu",["de"]="Menü",["zh"]="菜单",["ru"]="Меню",["ja"]="メニュー" },
        ["nav.secure"]   = new() { ["es"]="Conexión segura",["en"]="Secure connection",["pt"]="Conexão segura",["fr"]="Connexion sécurisée",["de"]="Sichere Verbindung",["zh"]="安全连接",["ru"]="Защищённое соединение",["ja"]="安全な接続" },

        // ── Vault extra strings ───────────────────────────────────────────
        ["vault.empty"]      = new() { ["es"]="No hay contraseñas guardadas.",["en"]="No passwords saved yet.",["pt"]="Nenhuma senha salva ainda.",["fr"]="Aucun mot de passe enregistré.",["de"]="Noch keine Passwörter gespeichert.",["zh"]="暂无保存的密码。",["ru"]="Паролей пока нет.",["ja"]="パスワードが保存されていません。" },
        ["vault.copy.user"]  = new() { ["es"]="Usuario",["en"]="User",["pt"]="Usuário",["fr"]="Utilisateur",["de"]="Benutzer",["zh"]="用户名",["ru"]="Польз.",["ja"]="ユーザー" },
        ["vault.copy.pwd"]   = new() { ["es"]="Password",["en"]="Password",["pt"]="Senha",["fr"]="Mot de passe",["de"]="Passwort",["zh"]="密码",["ru"]="Пароль",["ja"]="パスワード" },
        ["vault.copied.user"]= new() { ["es"]="Usuario copiado",["en"]="Username copied",["pt"]="Usuário copiado",["fr"]="Utilisateur copié",["de"]="Benutzername kopiert",["zh"]="用户名已复制",["ru"]="Логин скопирован",["ja"]="ユーザー名をコピーしました" },
        ["vault.copied.pwd"] = new() { ["es"]="Password copiado",["en"]="Password copied",["pt"]="Senha copiada",["fr"]="Mot de passe copié",["de"]="Passwort kopiert",["zh"]="密码已复制",["ru"]="Пароль скопирован",["ja"]="パスワードをコピーしました" },

        // ── New tab ───────────────────────────────────────────────────────
        ["newtab.title"] = new() { ["es"]="Nueva pestaña",["en"]="New tab",["pt"]="Nova aba",["fr"]="Nouvel onglet",["de"]="Neuer Tab",["zh"]="新标签页",["ru"]="Новая вкладка",["ja"]="新しいタブ" },
        ["newtab.search"] = new()
        {
            ["es"] = "Buscar o introducir dirección web",
            ["en"] = "Search or enter web address",
            ["pt"] = "Pesquisar ou inserir endereço web",
            ["fr"] = "Rechercher ou saisir une adresse web",
            ["de"] = "Suchen oder Webadresse eingeben",
            ["zh"] = "搜索或输入网址",
            ["ru"] = "Поиск или ввод адреса",
            ["ja"] = "検索またはURLを入力",
        },
        ["newtab.stats.empty"] = new()
        {
            ["es"] = "Navega por la web, aquí verás tus estadísticas de privacidad.",
            ["en"] = "Browse the web, your privacy stats will appear here.",
            ["pt"] = "Navegue pela web, suas estatísticas de privacidade aparecerão aqui.",
            ["fr"] = "Naviguez sur le web, vos statistiques de confidentialité apparaîtront ici.",
            ["de"] = "Surfe im Web, deine Datenschutz-Statistiken erscheinen hier.",
            ["zh"] = "浏览网页，您的隐私统计数据将显示在这里。",
            ["ru"] = "Просматривайте веб, здесь появится статистика конфиденциальности.",
            ["ja"] = "ウェブを閲覧すると、プライバシー統計がここに表示されます。",
        },

        // ── Sidebar ───────────────────────────────────────────────────────
        ["sidebar.newtab"]           = new() { ["es"]="+ Nueva pestaña",["en"]="+ New tab",["pt"]="+ Nova aba",["fr"]="+ Nouvel onglet",["de"]="+ Neuer Tab",["zh"]="+ 新标签",["ru"]="+ Новая вкладка",["ja"]="+ 新しいタブ" },
        ["sidebar.newtab.tooltip"]   = new() { ["es"]="Nueva pestaña (Ctrl+T)",["en"]="New tab (Ctrl+T)",["pt"]="Nova aba (Ctrl+T)",["fr"]="Nouvel onglet (Ctrl+T)",["de"]="Neuer Tab (Ctrl+T)",["zh"]="新标签页 (Ctrl+T)",["ru"]="Новая вкладка (Ctrl+T)",["ja"]="新しいタブ (Ctrl+T)" },
        ["sidebar.split.tooltip"]    = new() { ["es"]="Vista dividida (Ctrl+\\)",["en"]="Split view (Ctrl+\\)",["pt"]="Vista dividida (Ctrl+\\)",["fr"]="Vue partagée (Ctrl+\\)",["de"]="Geteilte Ansicht (Ctrl+\\)",["zh"]="分屏视图 (Ctrl+\\)",["ru"]="Разделённый вид (Ctrl+\\)",["ja"]="分割表示 (Ctrl+\\)" },
        ["sidebar.workspace.tooltip"]= new() { ["es"]="Nuevo workspace",["en"]="New workspace",["pt"]="Novo workspace",["fr"]="Nouvel espace de travail",["de"]="Neuer Arbeitsbereich",["zh"]="新工作区",["ru"]="Новое рабочее пространство",["ja"]="新しいワークスペース" },
        ["sidebar.collapse.tooltip"] = new() { ["es"]="Compactar barra lateral",["en"]="Collapse sidebar",["pt"]="Recolher barra lateral",["fr"]="Réduire la barre latérale",["de"]="Seitenleiste einklappen",["zh"]="收起侧栏",["ru"]="Свернуть боковую панель",["ja"]="サイドバーを折りたたむ" },
        ["sidebar.expand.tooltip"]   = new() { ["es"]="Expandir barra lateral",["en"]="Expand sidebar",["pt"]="Expandir barra lateral",["fr"]="Développer la barre latérale",["de"]="Seitenleiste aufklappen",["zh"]="展开侧栏",["ru"]="Развернуть боковую панель",["ja"]="サイドバーを展開する" },

        // ── Window titles ─────────────────────────────────────────────────
        ["title.settings"] = new()
        {
            ["es"] = "VELO — Configuración",
            ["en"] = "VELO — Settings",
            ["pt"] = "VELO — Configurações",
            ["fr"] = "VELO — Paramètres",
            ["de"] = "VELO — Einstellungen",
            ["zh"] = "VELO — 设置",
            ["ru"] = "VELO — Настройки",
            ["ja"] = "VELO — 設定",
        },
        ["title.history"] = new()
        {
            ["es"] = "Historial — VELO",
            ["en"] = "History — VELO",
            ["pt"] = "Histórico — VELO",
            ["fr"] = "Historique — VELO",
            ["de"] = "Verlauf — VELO",
            ["zh"] = "历史记录 — VELO",
            ["ru"] = "История — VELO",
            ["ja"] = "履歴 — VELO",
        },
        ["title.bookmarks"] = new()
        {
            ["es"] = "Marcadores — VELO",
            ["en"] = "Bookmarks — VELO",
            ["pt"] = "Favoritos — VELO",
            ["fr"] = "Favoris — VELO",
            ["de"] = "Lesezeichen — VELO",
            ["zh"] = "书签 — VELO",
            ["ru"] = "Закладки — VELO",
            ["ja"] = "ブックマーク — VELO",
        },
        ["title.downloads"] = new()
        {
            ["es"] = "Descargas — VELO",
            ["en"] = "Downloads — VELO",
            ["pt"] = "Downloads — VELO",
            ["fr"] = "Téléchargements — VELO",
            ["de"] = "Downloads — VELO",
            ["zh"] = "下载 — VELO",
            ["ru"] = "Загрузки — VELO",
            ["ja"] = "ダウンロード — VELO",
        },
        ["title.vault"] = new()
        {
            ["es"] = "VELO — Password Vault", ["en"] = "VELO — Password Vault",
            ["pt"] = "VELO — Cofre de Senhas", ["fr"] = "VELO — Coffre-fort",
            ["de"] = "VELO — Passwort-Tresor", ["zh"] = "VELO — 密码库",
            ["ru"] = "VELO — Хранилище паролей", ["ja"] = "VELO — パスワード保管庫",
        },
        ["title.malwaredex"] = new()
        {
            ["es"] = "Malwaredex — VELO", ["en"] = "Malwaredex — VELO",
            ["pt"] = "Malwaredex — VELO",  ["fr"] = "Malwaredex — VELO",
            ["de"] = "Malwaredex — VELO",  ["zh"] = "恶意软件图鉴 — VELO",
            ["ru"] = "Malwaredex — VELO",  ["ja"] = "マルウェア図鑑 — VELO",
        },
        ["title.cleardata"] = new()
        {
            ["es"] = "Limpiar datos — VELO",
            ["en"] = "Clear data — VELO",
            ["pt"] = "Limpar dados — VELO",
            ["fr"] = "Effacer données — VELO",
            ["de"] = "Daten löschen — VELO",
            ["zh"] = "清除数据 — VELO",
            ["ru"] = "Очистить данные — VELO",
            ["ja"] = "データ消去 — VELO",
        },

        // ── Malwaredex subtitles & card strings ───────────────────────────
        // {0}=possible
        ["malwaredex.zero"] = new() { ["es"]="0 de {0} capturados — navega para encontrar amenazas",["en"]="0 of {0} captured — browse to discover threats",["pt"]="0 de {0} capturados — navegue para encontrar ameaças",["fr"]="0 sur {0} capturés — naviguez pour trouver des menaces",["de"]="0 von {0} erfasst — surfe, um Bedrohungen zu finden",["zh"]="已捕获 0 / {0} — 浏览网页以发现威胁",["ru"]="0 из {0} поймано — открывайте сайты для обнаружения угроз",["ja"]="0 / {0} 捕獲済み — ブラウジングして脅威を発見しましょう" },
        // {0}=captured {1}=possible {2}=totalSeen
        ["malwaredex.captured"] = new() { ["es"]="{0} de {1} capturados · {2:N0} amenazas bloqueadas",["en"]="{0} of {1} captured · {2:N0} threats blocked",["pt"]="{0} de {1} capturados · {2:N0} ameaças bloqueadas",["fr"]="{0} sur {1} capturés · {2:N0} menaces bloquées",["de"]="{0} von {1} erfasst · {2:N0} Bedrohungen blockiert",["zh"]="已捕获 {0} / {1} · 已拦截 {2:N0} 个威胁",["ru"]="{0} из {1} поймано · {2:N0} угроз заблокировано",["ja"]="{0} / {1} 捕獲済み · {2:N0} 件の脅威をブロック" },
        // {0}=captured {1}=possible {2}=maxStage3 {3}=totalSeen
        ["malwaredex.finalform"] = new() { ["es"]="{0} de {1} capturados · {2} en Forma Final · {3:N0} bloqueadas",["en"]="{0} of {1} captured · {2} in Final Form · {3:N0} blocked",["pt"]="{0} de {1} capturados · {2} em Forma Final · {3:N0} bloqueadas",["fr"]="{0} sur {1} · {2} en Forme Finale · {3:N0} bloquées",["de"]="{0} von {1} · {2} in Endform · {3:N0} blockiert",["zh"]="已捕获 {0} / {1} · {2} 个终极形态 · 已拦截 {3:N0}",["ru"]="{0} из {1} · {2} в финальной форме · {3:N0} заблок.",["ja"]="{0} / {1} 捕獲 · 最終形態 {2} 体 · {3:N0} ブロック" },
        ["malwaredex.locked"] = new() { ["es"]="Sin capturar",["en"]="Not captured",["pt"]="Não capturado",["fr"]="Non capturé",["de"]="Nicht erfasst",["zh"]="未捕获",["ru"]="Не поймано",["ja"]="未捕獲" },
        ["malwaredex.stage3"] = new() { ["es"]="FORMA FINAL",["en"]="FINAL FORM",["pt"]="FORMA FINAL",["fr"]="FORME FINALE",["de"]="ENDFORM",["zh"]="终极形态",["ru"]="ФИНАЛЬНАЯ ФОРМА",["ja"]="最終形態" },

        // ── Malwaredex category labels ────────────────────────────────────
        ["mdx.KnownTracker"]       = new() { ["es"]="Rastreadores Conocidos",["en"]="Known Trackers",["pt"]="Rastreadores Conhecidos",["fr"]="Traceurs connus",["de"]="Bekannte Tracker",["zh"]="已知追踪器",["ru"]="Известные трекеры",["ja"]="既知のトラッカー" },
        ["mdx.Tracker"]            = new() { ["es"]="Rastreadores de Conducta",["en"]="Behavioral Trackers",["pt"]="Rastreadores Comportamentais",["fr"]="Traceurs comportementaux",["de"]="Verhaltens-Tracker",["zh"]="行为追踪器",["ru"]="Поведенческие трекеры",["ja"]="行動トラッカー" },
        ["mdx.Fingerprinting"]     = new() { ["es"]="Huella Digital",["en"]="Fingerprinting",["pt"]="Impressão Digital",["fr"]="Empreinte numérique",["de"]="Fingerabdruck",["zh"]="指纹识别",["ru"]="Цифровой отпечаток",["ja"]="フィンガープリンティング" },
        ["mdx.MixedContent"]       = new() { ["es"]="Contenido Mixto",["en"]="Mixed Content",["pt"]="Conteúdo Misto",["fr"]="Contenu mixte",["de"]="Gemischter Inhalt",["zh"]="混合内容",["ru"]="Смешанный контент",["ja"]="混合コンテンツ" },
        ["mdx.Malware"]            = new() { ["es"]="Malware",["en"]="Malware",["pt"]="Malware",["fr"]="Malware",["de"]="Malware",["zh"]="恶意软件",["ru"]="Вредоносное ПО",["ja"]="マルウェア" },
        ["mdx.Phishing"]           = new() { ["es"]="Phishing",["en"]="Phishing",["pt"]="Phishing",["fr"]="Hameçonnage",["de"]="Phishing",["zh"]="钓鱼攻击",["ru"]="Фишинг",["ja"]="フィッシング" },
        ["mdx.DataExfiltration"]   = new() { ["es"]="Exfiltración de Datos",["en"]="Data Exfiltration",["pt"]="Exfiltração de Dados",["fr"]="Exfiltration de données",["de"]="Datenexfiltration",["zh"]="数据窃取",["ru"]="Утечка данных",["ja"]="データ窃取" },
        ["mdx.Miner"]              = new() { ["es"]="Mineros Cripto",["en"]="Crypto Miners",["pt"]="Mineradores Cripto",["fr"]="Mineurs crypto",["de"]="Krypto-Miner",["zh"]="加密矿工",["ru"]="Криптомайнеры",["ja"]="仮想通貨マイナー" },
        ["mdx.MitM"]               = new() { ["es"]="Ataques de Red",["en"]="Network Attacks",["pt"]="Ataques de Rede",["fr"]="Attaques réseau",["de"]="Netzwerkangriffe",["zh"]="网络攻击",["ru"]="Сетевые атаки",["ja"]="ネットワーク攻撃" },
        ["mdx.DnsRebinding"]       = new() { ["es"]="DNS Rebinding",["en"]="DNS Rebinding",["pt"]="DNS Rebinding",["fr"]="DNS Rebinding",["de"]="DNS-Rebinding",["zh"]="DNS 重绑定",["ru"]="DNS-rebinding",["ja"]="DNSリバインディング" },
        ["mdx.SSRF"]               = new() { ["es"]="SSRF",["en"]="SSRF",["pt"]="SSRF",["fr"]="SSRF",["de"]="SSRF",["zh"]="SSRF",["ru"]="SSRF",["ja"]="SSRF" },
        ["mdx.ContainerViolation"] = new() { ["es"]="Violaciones de Contenedor",["en"]="Container Violations",["pt"]="Violações de Contêiner",["fr"]="Violations de conteneur",["de"]="Container-Verstöße",["zh"]="容器违规",["ru"]="Нарушения контейнера",["ja"]="コンテナ違反" },
        ["mdx.Other"]              = new() { ["es"]="Otras Amenazas",["en"]="Other Threats",["pt"]="Outras Ameaças",["fr"]="Autres menaces",["de"]="Andere Bedrohungen",["zh"]="其他威胁",["ru"]="Прочие угрозы",["ja"]="その他の脅威" },
    };
}
