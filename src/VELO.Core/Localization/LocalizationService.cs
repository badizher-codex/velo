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

        // ── Malwaredex empty / loading states ─────────────────────────────
        ["malwaredex.empty.title"] = new() { ["es"]="Ninguna amenaza capturada todavía",["en"]="No threats captured yet",["pt"]="Nenhuma ameaça capturada ainda",["fr"]="Aucune menace capturée pour l'instant",["de"]="Noch keine Bedrohungen erfasst",["zh"]="尚未捕获任何威胁",["ru"]="Угрозы ещё не обнаружены",["ja"]="まだ脅威は捕獲されていません" },
        ["malwaredex.empty.desc"]  = new() { ["es"]="Navega en la web y VELO registrará aquí cada tipo de amenaza bloqueada.",["en"]="Browse the web and VELO will log each type of blocked threat here.",["pt"]="Navegue pela web e o VELO registrará aqui cada tipo de ameaça bloqueada.",["fr"]="Naviguez sur le web et VELO enregistrera ici chaque type de menace bloquée.",["de"]="Surfe im Web und VELO protokolliert hier jeden blockierten Bedrohungstyp.",["zh"]="浏览网页，VELO 将在此记录每种被拦截的威胁。",["ru"]="Просматривайте сайты, и VELO будет записывать сюда каждый заблокированный тип угроз.",["ja"]="ウェブを閲覧すると、VELOがブロックされた脅威の種類をここに記録します。" },
        ["malwaredex.loading"]     = new() { ["es"]="Cargando Malwaredex…",["en"]="Loading Malwaredex…",["pt"]="Carregando Malwaredex…",["fr"]="Chargement du Malwaredex…",["de"]="Malwaredex wird geladen…",["zh"]="正在加载 Malwaredex…",["ru"]="Загрузка Malwaredex…",["ja"]="Malwaredex を読み込み中…" },
        ["malwaredex.footer"]      = new() { ["es"]="★☆☆ Primera captura · ★★☆ Evolución · ★★★ Forma Final",["en"]="★☆☆ First capture · ★★☆ Evolution · ★★★ Final Form",["pt"]="★☆☆ Primeira captura · ★★☆ Evolução · ★★★ Forma Final",["fr"]="★☆☆ Première capture · ★★☆ Évolution · ★★★ Forme Finale",["de"]="★☆☆ Erstfund · ★★☆ Entwicklung · ★★★ Endform",["zh"]="★☆☆ 首次捕获 · ★★☆ 进化 · ★★★ 终极形态",["ru"]="★☆☆ Первый улов · ★★☆ Эволюция · ★★★ Финальная форма",["ja"]="★☆☆ 初捕獲 · ★★☆ 進化 · ★★★ 最終形態" },

        // ── Sidebar — container assignment context menu ───────────────────
        ["sidebar.container.assign"]   = new() { ["es"]="Asignar container",["en"]="Assign container",["pt"]="Atribuir contêiner",["fr"]="Attribuer un conteneur",["de"]="Container zuweisen",["zh"]="分配容器",["ru"]="Назначить контейнер",["ja"]="コンテナを割り当て" },
        ["sidebar.container.none"]     = new() { ["es"]="Sin container",["en"]="No container",["pt"]="Sem contêiner",["fr"]="Sans conteneur",["de"]="Kein Container",["zh"]="无容器",["ru"]="Без контейнера",["ja"]="コンテナなし" },
        ["sidebar.container.personal"] = new() { ["es"]="Personal",["en"]="Personal",["pt"]="Pessoal",["fr"]="Personnel",["de"]="Persönlich",["zh"]="个人",["ru"]="Личное",["ja"]="個人" },
        ["sidebar.container.work"]     = new() { ["es"]="Trabajo",["en"]="Work",["pt"]="Trabalho",["fr"]="Travail",["de"]="Arbeit",["zh"]="工作",["ru"]="Работа",["ja"]="仕事" },
        ["sidebar.container.banking"]  = new() { ["es"]="Banca",["en"]="Banking",["pt"]="Banco",["fr"]="Banque",["de"]="Banking",["zh"]="银行",["ru"]="Банк",["ja"]="銀行" },
        ["sidebar.container.shopping"] = new() { ["es"]="Compras",["en"]="Shopping",["pt"]="Compras",["fr"]="Shopping",["de"]="Einkaufen",["zh"]="购物",["ru"]="Покупки",["ja"]="ショッピング" },
        ["sidebar.container.moveto"]   = new() { ["es"]="Mover a workspace",["en"]="Move to workspace",["pt"]="Mover para workspace",["fr"]="Déplacer vers l'espace de travail",["de"]="In Arbeitsbereich verschieben",["zh"]="移至工作区",["ru"]="Переместить в рабочее пространство",["ja"]="ワークスペースへ移動" },

        // ── Security Inspector — section titles ───────────────────────────
        ["inspector.section.tls"]         = new() { ["es"]="TLS / CERTIFICADO",["en"]="TLS / CERTIFICATE",["pt"]="TLS / CERTIFICADO",["fr"]="TLS / CERTIFICAT",["de"]="TLS / ZERTIFIKAT",["zh"]="TLS / 证书",["ru"]="TLS / СЕРТИФИКАТ",["ja"]="TLS / 証明書" },
        ["inspector.section.blocks"]      = new() { ["es"]="BLOQUEOS EN ESTA SESIÓN",["en"]="BLOCKS IN THIS SESSION",["pt"]="BLOQUEIOS NESTA SESSÃO",["fr"]="BLOCAGES DANS CETTE SESSION",["de"]="SPERREN IN DIESER SITZUNG",["zh"]="本次会话的拦截",["ru"]="БЛОКИРОВКИ В ЭТОЙ СЕССИИ",["ja"]="このセッションのブロック" },
        ["inspector.section.ai"]          = new() { ["es"]="ANÁLISIS DE INTELIGENCIA ARTIFICIAL",["en"]="ARTIFICIAL INTELLIGENCE ANALYSIS",["pt"]="ANÁLISE DE INTELIGÊNCIA ARTIFICIAL",["fr"]="ANALYSE PAR INTELLIGENCE ARTIFICIELLE",["de"]="ANALYSE DURCH KÜNSTLICHE INTELLIGENZ",["zh"]="人工智能分析",["ru"]="АНАЛИЗ ИСКУССТВЕННОГО ИНТЕЛЛЕКТА",["ja"]="人工知能分析" },
        ["inspector.section.fingerprint"] = new() { ["es"]="PROTECCIÓN DE HUELLA DIGITAL",["en"]="FINGERPRINT PROTECTION",["pt"]="PROTEÇÃO DE IMPRESSÃO DIGITAL",["fr"]="PROTECTION D'EMPREINTE NUMÉRIQUE",["de"]="FINGERABDRUCK-SCHUTZ",["zh"]="指纹保护",["ru"]="ЗАЩИТА ОТ FINGERPRINTING",["ja"]="フィンガープリント保護" },
        ["inspector.section.score"]       = new() { ["es"]="DETALLES DEL SHIELD SCORE",["en"]="SHIELD SCORE DETAILS",["pt"]="DETALHES DO SHIELD SCORE",["fr"]="DÉTAILS DU SHIELD SCORE",["de"]="SHIELD SCORE DETAILS",["zh"]="防护评分详情",["ru"]="ДЕТАЛИ SHIELD SCORE",["ja"]="シールドスコア詳細" },

        // ── Security Inspector — field labels ─────────────────────────────
        ["inspector.tls.status"]       = new() { ["es"]="Estado TLS",["en"]="TLS Status",["pt"]="Status TLS",["fr"]="État TLS",["de"]="TLS-Status",["zh"]="TLS 状态",["ru"]="Статус TLS",["ja"]="TLS ステータス" },
        ["inspector.tls.indicator"]    = new() { ["es"]="Indicador de URL",["en"]="URL Indicator",["pt"]="Indicador de URL",["fr"]="Indicateur d'URL",["de"]="URL-Indikator",["zh"]="URL 指示器",["ru"]="Индикатор URL",["ja"]="URL インジケーター" },
        ["inspector.tls.lock"]         = new() { ["es"]="Candado cerrado 🔒",["en"]="Closed lock 🔒",["pt"]="Cadeado fechado 🔒",["fr"]="Cadenas fermé 🔒",["de"]="Geschlossenes Schloss 🔒",["zh"]="锁定 🔒",["ru"]="Закрытый замок 🔒",["ja"]="施錠済み 🔒" },
        ["inspector.tls.warning"]      = new() { ["es"]="Advertencia ⚠️",["en"]="Warning ⚠️",["pt"]="Aviso ⚠️",["fr"]="Avertissement ⚠️",["de"]="Warnung ⚠️",["zh"]="警告 ⚠️",["ru"]="Предупреждение ⚠️",["ja"]="警告 ⚠️" },
        ["inspector.blocks.trackers"]  = new() { ["es"]="Rastreadores bloqueados",["en"]="Blocked trackers",["pt"]="Rastreadores bloqueados",["fr"]="Traceurs bloqués",["de"]="Blockierte Tracker",["zh"]="已拦截的跟踪器",["ru"]="Заблокированные трекеры",["ja"]="ブロックされたトラッカー" },
        ["inspector.blocks.scripts"]   = new() { ["es"]="Scripts sospechosos",["en"]="Suspicious scripts",["pt"]="Scripts suspeitos",["fr"]="Scripts suspects",["de"]="Verdächtige Skripte",["zh"]="可疑脚本",["ru"]="Подозрительные скрипты",["ja"]="不審なスクリプト" },
        ["inspector.blocks.malware"]   = new() { ["es"]="Malware / phishing",["en"]="Malware / phishing",["pt"]="Malware / phishing",["fr"]="Malware / hameçonnage",["de"]="Malware / Phishing",["zh"]="恶意软件 / 钓鱼",["ru"]="Вредонос / фишинг",["ja"]="マルウェア / フィッシング" },
        ["inspector.blocks.none"]      = new() { ["es"]="Ninguno detectado",["en"]="None detected",["pt"]="Nenhum detectado",["fr"]="Aucun détecté",["de"]="Keiner erkannt",["zh"]="未检测到",["ru"]="Не обнаружено",["ja"]="検出なし" },
        ["inspector.blocks.nonescript"]= new() { ["es"]="Ninguno",["en"]="None",["pt"]="Nenhum",["fr"]="Aucun",["de"]="Keine",["zh"]="无",["ru"]="Нет",["ja"]="なし" },
        ["inspector.blocks.nomalware"] = new() { ["es"]="No detectado",["en"]="Not detected",["pt"]="Não detectado",["fr"]="Non détecté",["de"]="Nicht erkannt",["zh"]="未检测到",["ru"]="Не обнаружено",["ja"]="未検出" },
        ["inspector.ai.status.label"]  = new() { ["es"]="Estado",["en"]="Status",["pt"]="Estado",["fr"]="État",["de"]="Status",["zh"]="状态",["ru"]="Статус",["ja"]="ステータス" },
        ["inspector.ai.noanalysis"]    = new() { ["es"]="Sin análisis disponible para esta sesión",["en"]="No analysis available for this session",["pt"]="Nenhuma análise disponível para esta sessão",["fr"]="Aucune analyse disponible pour cette session",["de"]="Keine Analyse für diese Sitzung verfügbar",["zh"]="本次会话暂无分析数据",["ru"]="Анализ для этой сессии недоступен",["ja"]="このセッションの分析データはありません" },
        ["inspector.ai.verdict"]       = new() { ["es"]="Veredicto",["en"]="Verdict",["pt"]="Veredicto",["fr"]="Verdict",["de"]="Urteil",["zh"]="判定",["ru"]="Вердикт",["ja"]="判定" },
        ["inspector.ai.confidence"]    = new() { ["es"]="Confianza",["en"]="Confidence",["pt"]="Confiança",["fr"]="Confiance",["de"]="Konfidenz",["zh"]="置信度",["ru"]="Уверенность",["ja"]="信頼度" },
        ["inspector.ai.engine"]        = new() { ["es"]="Motor",["en"]="Engine",["pt"]="Motor",["fr"]="Moteur",["de"]="Engine",["zh"]="引擎",["ru"]="Движок",["ja"]="エンジン" },
        ["inspector.ai.reason"]        = new() { ["es"]="Razón",["en"]="Reason",["pt"]="Razão",["fr"]="Raison",["de"]="Grund",["zh"]="原因",["ru"]="Причина",["ja"]="理由" },
        ["inspector.fp.status"]        = new() { ["es"]="Estado",["en"]="Status",["pt"]="Estado",["fr"]="État",["de"]="Status",["zh"]="状态",["ru"]="Статус",["ja"]="状態" },
        ["inspector.fp.active"]        = new() { ["es"]="Activa — Nivel:",["en"]="Active — Level:",["pt"]="Ativa — Nível:",["fr"]="Actif — Niveau :",["de"]="Aktiv — Stufe:",["zh"]="已启用 — 级别：",["ru"]="Активна — Уровень:",["ja"]="有効 — レベル：" },
        ["inspector.fp.inactive"]      = new() { ["es"]="Desactivada en Configuración",["en"]="Disabled in Settings",["pt"]="Desativada nas Configurações",["fr"]="Désactivée dans les Paramètres",["de"]="In den Einstellungen deaktiviert",["zh"]="已在设置中禁用",["ru"]="Отключена в настройках",["ja"]="設定で無効化されています" },
        ["inspector.fp.canvas.value"]  = new() { ["es"]="Ruido aleatorio inyectado",["en"]="Random noise injected",["pt"]="Ruído aleatório injetado",["fr"]="Bruit aléatoire injecté",["de"]="Zufälliges Rauschen eingefügt",["zh"]="已注入随机噪声",["ru"]="Случайный шум внедрён",["ja"]="ランダムノイズを注入済み" },
        ["inspector.fp.webgl.value"]   = new() { ["es"]="Renderer falso activo",["en"]="Fake renderer active",["pt"]="Renderer falso ativo",["fr"]="Faux renderer actif",["de"]="Gefälschter Renderer aktiv",["zh"]="虚假渲染器已激活",["ru"]="Поддельный рендерер активен",["ja"]="フェイクレンダラーが有効" },
        ["inspector.fp.audio.value"]   = new() { ["es"]="Ruido en AudioContext",["en"]="Noise in AudioContext",["pt"]="Ruído no AudioContext",["fr"]="Bruit dans AudioContext",["de"]="Rauschen in AudioContext",["zh"]="AudioContext 中注入噪声",["ru"]="Шум в AudioContext",["ja"]="AudioContext にノイズを注入" },
        ["inspector.fp.webrtc.value"]  = new() { ["es"]="IPs locales ocultas",["en"]="Local IPs hidden",["pt"]="IPs locais ocultas",["fr"]="IPs locales masquées",["de"]="Lokale IPs verborgen",["zh"]="本地 IP 已隐藏",["ru"]="Локальные IP скрыты",["ja"]="ローカル IP を非表示" },
        ["inspector.shield.gold"]      = new() { ["es"]="Excelente (Gold)",["en"]="Excellent (Gold)",["pt"]="Excelente (Ouro)",["fr"]="Excellent (Or)",["de"]="Ausgezeichnet (Gold)",["zh"]="优秀（金级）",["ru"]="Отлично (Gold)",["ja"]="優秀 (Gold)" },
        ["inspector.shield.green"]     = new() { ["es"]="Seguro (Verde)",["en"]="Safe (Green)",["pt"]="Seguro (Verde)",["fr"]="Sûr (Vert)",["de"]="Sicher (Grün)",["zh"]="安全（绿色）",["ru"]="Безопасно (Зелёный)",["ja"]="安全 (グリーン)" },
        ["inspector.shield.yellow"]    = new() { ["es"]="Precaución (Amarillo)",["en"]="Caution (Yellow)",["pt"]="Precaução (Amarelo)",["fr"]="Prudence (Jaune)",["de"]="Vorsicht (Gelb)",["zh"]="注意（黄色）",["ru"]="Осторожно (Жёлтый)",["ja"]="注意 (イエロー)" },
        ["inspector.shield.red"]       = new() { ["es"]="Peligro (Rojo)",["en"]="Danger (Red)",["pt"]="Perigo (Vermelho)",["fr"]="Danger (Rouge)",["de"]="Gefahr (Rot)",["zh"]="危险（红色）",["ru"]="Опасно (Красный)",["ja"]="危険 (レッド)" },
        ["inspector.shield.analyzing"] = new() { ["es"]="Analizando…",["en"]="Analyzing…",["pt"]="Analisando…",["fr"]="Analyse en cours…",["de"]="Analysiere…",["zh"]="分析中…",["ru"]="Анализирую…",["ja"]="分析中…" },
        ["inspector.updated"]          = new() { ["es"]="Actualizado:",["en"]="Updated:",["pt"]="Atualizado:",["fr"]="Mis à jour :",["de"]="Aktualisiert:",["zh"]="已更新：",["ru"]="Обновлено:",["ja"]="更新済み：" },
        ["inspector.btn.devtools"]     = new() { ["es"]="🔧 Abrir DevTools nativos",["en"]="🔧 Open native DevTools",["pt"]="🔧 Abrir DevTools nativos",["fr"]="🔧 Ouvrir les DevTools natifs",["de"]="🔧 Native DevTools öffnen",["zh"]="🔧 打开原生 DevTools",["ru"]="🔧 Открыть DevTools",["ja"]="🔧 ネイティブ DevTools を開く" },
        ["inspector.btn.export"]       = new() { ["es"]="📋 Exportar JSON",["en"]="📋 Export JSON",["pt"]="📋 Exportar JSON",["fr"]="📋 Exporter JSON",["de"]="📋 JSON exportieren",["zh"]="📋 导出 JSON",["ru"]="📋 Экспорт JSON",["ja"]="📋 JSON エクスポート" },
        ["inspector.btn.rescan"]       = new() { ["es"]="🔄 Re-escanear",["en"]="🔄 Re-scan",["pt"]="🔄 Re-escanear",["fr"]="🔄 Ré-analyser",["de"]="🔄 Neu scannen",["zh"]="🔄 重新扫描",["ru"]="🔄 Повторить сканирование",["ja"]="🔄 再スキャン" },
        ["inspector.btn.scanning"]     = new() { ["es"]="⏳ Escaneando…",["en"]="⏳ Scanning…",["pt"]="⏳ Escaneando…",["fr"]="⏳ Analyse en cours…",["de"]="⏳ Scanne…",["zh"]="⏳ 扫描中…",["ru"]="⏳ Сканирую…",["ja"]="⏳ スキャン中…" },
        ["inspector.btn.updated"]      = new() { ["es"]="✓ Actualizado",["en"]="✓ Updated",["pt"]="✓ Atualizado",["fr"]="✓ Mis à jour",["de"]="✓ Aktualisiert",["zh"]="✓ 已更新",["ru"]="✓ Обновлено",["ja"]="✓ 更新済み" },
        ["inspector.export.title"]     = new() { ["es"]="Exportar análisis de seguridad",["en"]="Export security analysis",["pt"]="Exportar análise de segurança",["fr"]="Exporter l'analyse de sécurité",["de"]="Sicherheitsanalyse exportieren",["zh"]="导出安全分析",["ru"]="Экспорт анализа безопасности",["ja"]="セキュリティ分析をエクスポート" },

        // ── Vault — main list buttons ─────────────────────────────────────
        ["vault.add"]              = new() { ["es"]="+ Agregar",["en"]="+ Add",["pt"]="+ Adicionar",["fr"]="+ Ajouter",["de"]="+ Hinzufügen",["zh"]="+ 添加",["ru"]="+ Добавить",["ja"]="+ 追加" },
        ["vault.lock"]             = new() { ["es"]="🔒 Bloquear",["en"]="🔒 Lock",["pt"]="🔒 Bloquear",["fr"]="🔒 Verrouiller",["de"]="🔒 Sperren",["zh"]="🔒 锁定",["ru"]="🔒 Заблокировать",["ja"]="🔒 ロック" },

        // ── Vault — edit form ─────────────────────────────────────────────
        ["vault.form.new"]         = new() { ["es"]="Nueva entrada",["en"]="New entry",["pt"]="Nova entrada",["fr"]="Nouvelle entrée",["de"]="Neuer Eintrag",["zh"]="新建条目",["ru"]="Новая запись",["ja"]="新しいエントリ" },
        ["vault.form.edit"]        = new() { ["es"]="Editar entrada",["en"]="Edit entry",["pt"]="Editar entrada",["fr"]="Modifier l'entrée",["de"]="Eintrag bearbeiten",["zh"]="编辑条目",["ru"]="Редактировать запись",["ja"]="エントリを編集" },
        ["vault.form.back"]        = new() { ["es"]="← Volver",["en"]="← Back",["pt"]="← Voltar",["fr"]="← Retour",["de"]="← Zurück",["zh"]="← 返回",["ru"]="← Назад",["ja"]="← 戻る" },
        ["vault.form.site"]        = new() { ["es"]="Sitio",["en"]="Site",["pt"]="Site",["fr"]="Site",["de"]="Seite",["zh"]="网站",["ru"]="Сайт",["ja"]="サイト" },
        ["vault.form.url"]         = new() { ["es"]="URL",["en"]="URL",["pt"]="URL",["fr"]="URL",["de"]="URL",["zh"]="URL",["ru"]="URL",["ja"]="URL" },
        ["vault.form.username"]    = new() { ["es"]="Usuario / Email",["en"]="Username / Email",["pt"]="Usuário / Email",["fr"]="Utilisateur / E-mail",["de"]="Benutzer / E-Mail",["zh"]="用户名 / 邮箱",["ru"]="Пользователь / Email",["ja"]="ユーザー名 / メール" },
        ["vault.form.password"]    = new() { ["es"]="Contraseña",["en"]="Password",["pt"]="Senha",["fr"]="Mot de passe",["de"]="Passwort",["zh"]="密码",["ru"]="Пароль",["ja"]="パスワード" },
        ["vault.form.notes"]       = new() { ["es"]="Notas (opcional)",["en"]="Notes (optional)",["pt"]="Notas (opcional)",["fr"]="Notes (optionnel)",["de"]="Notizen (optional)",["zh"]="备注（可选）",["ru"]="Заметки (необязательно)",["ja"]="メモ（任意）" },
        ["vault.form.save"]        = new() { ["es"]="Guardar",["en"]="Save",["pt"]="Salvar",["fr"]="Enregistrer",["de"]="Speichern",["zh"]="保存",["ru"]="Сохранить",["ja"]="保存" },
        ["vault.form.cancel"]      = new() { ["es"]="Cancelar",["en"]="Cancel",["pt"]="Cancelar",["fr"]="Annuler",["de"]="Abbrechen",["zh"]="取消",["ru"]="Отмена",["ja"]="キャンセル" },
        ["vault.form.delete"]      = new() { ["es"]="🗑 Eliminar",["en"]="🗑 Delete",["pt"]="🗑 Excluir",["fr"]="🗑 Supprimer",["de"]="🗑 Löschen",["zh"]="🗑 删除",["ru"]="🗑 Удалить",["ja"]="🗑 削除" },
        ["vault.form.required"]    = new() { ["es"]="✦ Este campo es obligatorio",["en"]="✦ This field is required",["pt"]="✦ Este campo é obrigatório",["fr"]="✦ Ce champ est obligatoire",["de"]="✦ Dieses Feld ist erforderlich",["zh"]="✦ 此字段为必填项",["ru"]="✦ Это поле обязательно",["ja"]="✦ このフィールドは必須です" },
        ["vault.form.toggle"]      = new() { ["es"]="Mostrar/Ocultar",["en"]="Show/Hide",["pt"]="Mostrar/Ocultar",["fr"]="Afficher/Masquer",["de"]="Anzeigen/Verbergen",["zh"]="显示/隐藏",["ru"]="Показать/скрыть",["ja"]="表示/非表示" },
        ["vault.form.generate"]    = new() { ["es"]="⚡ Generar",["en"]="⚡ Generate",["pt"]="⚡ Gerar",["fr"]="⚡ Générer",["de"]="⚡ Generieren",["zh"]="⚡ 生成",["ru"]="⚡ Сгенерировать",["ja"]="⚡ 生成" },
        ["vault.form.generator"]   = new() { ["es"]="Generador de contraseña",["en"]="Password generator",["pt"]="Gerador de senha",["fr"]="Générateur de mot de passe",["de"]="Passwort-Generator",["zh"]="密码生成器",["ru"]="Генератор паролей",["ja"]="パスワードジェネレーター" },
        ["vault.form.length"]      = new() { ["es"]="Longitud:",["en"]="Length:",["pt"]="Comprimento:",["fr"]="Longueur :",["de"]="Länge:",["zh"]="长度：",["ru"]="Длина:",["ja"]="長さ：" },
        ["vault.form.uppercase"]   = new() { ["es"]="Mayúsculas",["en"]="Uppercase",["pt"]="Maiúsculas",["fr"]="Majuscules",["de"]="Großbuchstaben",["zh"]="大写字母",["ru"]="Заглавные",["ja"]="大文字" },
        ["vault.form.numbers"]     = new() { ["es"]="Números",["en"]="Numbers",["pt"]="Números",["fr"]="Chiffres",["de"]="Zahlen",["zh"]="数字",["ru"]="Цифры",["ja"]="数字" },
        ["vault.form.symbols"]     = new() { ["es"]="Símbolos",["en"]="Symbols",["pt"]="Símbolos",["fr"]="Symboles",["de"]="Symbole",["zh"]="符号",["ru"]="Символы",["ja"]="記号" },
        ["vault.form.regenerate"]  = new() { ["es"]="Regenerar",["en"]="Regenerate",["pt"]="Regenerar",["fr"]="Régénérer",["de"]="Neu generieren",["zh"]="重新生成",["ru"]="Перегенерировать",["ja"]="再生成" },

        // ── Security Panel (v2.0.5.2) ───────────────────────────────────
        ["security.verdict.block"]    = new() { ["es"]="AMENAZA BLOQUEADA",["en"]="THREAT BLOCKED",["pt"]="AMEAÇA BLOQUEADA",["fr"]="MENACE BLOQUÉE",["de"]="BEDROHUNG BLOCKIERT",["zh"]="威胁已阻止",["ru"]="УГРОЗА ЗАБЛОКИРОВАНА",["ja"]="脅威をブロック" },
        ["security.verdict.warn"]     = new() { ["es"]="ADVERTENCIA",["en"]="WARNING",["pt"]="AVISO",["fr"]="AVERTISSEMENT",["de"]="WARNUNG",["zh"]="警告",["ru"]="ПРЕДУПРЕЖДЕНИЕ",["ja"]="警告" },
        ["security.verdict.safe"]     = new() { ["es"]="SEGURO",["en"]="SAFE",["pt"]="SEGURO",["fr"]="SÛR",["de"]="SICHER",["zh"]="安全",["ru"]="БЕЗОПАСНО",["ja"]="安全" },
        ["security.what_happened"]    = new() { ["es"]="¿Qué pasó?",["en"]="What happened?",["pt"]="O que aconteceu?",["fr"]="Que s'est-il passé ?",["de"]="Was ist passiert?",["zh"]="发生了什么？",["ru"]="Что произошло?",["ja"]="何が起きた？" },
        ["security.why_blocked"]      = new() { ["es"]="¿Por qué lo bloqueé?",["en"]="Why was it blocked?",["pt"]="Por que foi bloqueado?",["fr"]="Pourquoi a-t-il été bloqué ?",["de"]="Warum wurde es blockiert?",["zh"]="为什么被阻止？",["ru"]="Почему заблокировано?",["ja"]="なぜブロックされた？" },
        ["security.what_means"]       = new() { ["es"]="¿Qué significa para ti?",["en"]="What does it mean for you?",["pt"]="O que significa para você?",["fr"]="Qu'est-ce que cela signifie pour vous ?",["de"]="Was bedeutet das für Sie?",["zh"]="这对您意味着什么？",["ru"]="Что это значит для вас?",["ja"]="あなたにとっての意味は？" },
        ["security.learn_more"]       = new() { ["es"]="¿Cómo puedo aprender más? ↗",["en"]="How can I learn more? ↗",["pt"]="Como posso saber mais? ↗",["fr"]="Comment en savoir plus ? ↗",["de"]="Wie kann ich mehr erfahren? ↗",["zh"]="如何了解更多？↗",["ru"]="Узнать больше ↗",["ja"]="もっと詳しく ↗" },
        ["security.tech_details"]     = new() { ["es"]="Detalles técnicos",["en"]="Technical details",["pt"]="Detalhes técnicos",["fr"]="Détails techniques",["de"]="Technische Details",["zh"]="技术详情",["ru"]="Технические детали",["ja"]="技術的な詳細" },
        ["security.label.type"]       = new() { ["es"]="Tipo:",["en"]="Type:",["pt"]="Tipo:",["fr"]="Type :",["de"]="Typ:",["zh"]="类型：",["ru"]="Тип:",["ja"]="種類：" },
        ["security.label.source"]     = new() { ["es"]="Fuente:",["en"]="Source:",["pt"]="Fonte:",["fr"]="Source :",["de"]="Quelle:",["zh"]="来源：",["ru"]="Источник:",["ja"]="ソース：" },
        ["security.label.confidence"] = new() { ["es"]="Confianza:",["en"]="Confidence:",["pt"]="Confiança:",["fr"]="Confiance :",["de"]="Vertrauen:",["zh"]="置信度：",["ru"]="Уверенность:",["ja"]="信頼度：" },
        ["security.label.score"]      = new() { ["es"]="Score:",["en"]="Score:",["pt"]="Pontuação:",["fr"]="Score :",["de"]="Bewertung:",["zh"]="分数：",["ru"]="Оценка:",["ja"]="スコア：" },
        ["security.false_positive"]   = new() { ["es"]="✋ ¿Fue un error? Reportar falso positivo",["en"]="✋ Was this a mistake? Report a false positive",["pt"]="✋ Foi um erro? Reportar falso positivo",["fr"]="✋ Était-ce une erreur ? Signaler un faux positif",["de"]="✋ War das ein Fehler? Falsch-Positiv melden",["zh"]="✋ 是错误吗？报告误报",["ru"]="✋ Это ошибка? Сообщить о ложном срабатывании",["ja"]="✋ 誤検知？誤検出を報告" },
        ["security.allow_once"]       = new() { ["es"]="Permitir una vez",["en"]="Allow once",["pt"]="Permitir uma vez",["fr"]="Autoriser une fois",["de"]="Einmal erlauben",["zh"]="允许一次",["ru"]="Разрешить один раз",["ja"]="一度だけ許可" },
        ["security.whitelist"]        = new() { ["es"]="Whitelist siempre",["en"]="Whitelist always",["pt"]="Whitelist sempre",["fr"]="Toujours autoriser",["de"]="Immer erlauben",["zh"]="始终允许",["ru"]="Всегда разрешать",["ja"]="常に許可" },
        ["security.minimize"]         = new() { ["es"]="Minimizar",["en"]="Minimize",["pt"]="Minimizar",["fr"]="Réduire",["de"]="Minimieren",["zh"]="最小化",["ru"]="Свернуть",["ja"]="最小化" },
        ["security.close"]            = new() { ["es"]="Cerrar",["en"]="Close",["pt"]="Fechar",["fr"]="Fermer",["de"]="Schließen",["zh"]="关闭",["ru"]="Закрыть",["ja"]="閉じる" },
        ["security.countdown"]        = new() { ["es"]="Cerrando en {0}s…",["en"]="Closing in {0}s…",["pt"]="Fechando em {0}s…",["fr"]="Fermeture dans {0}s…",["de"]="Schließt in {0}s…",["zh"]="{0}秒后关闭…",["ru"]="Закрытие через {0}с…",["ja"]="{0}秒後に閉じます…" },
        ["security.events_grouped"]   = new() { ["es"]="🔴 {0} eventos similares bloqueados de {1} en los últimos 30 segundos.",["en"]="🔴 {0} similar events blocked from {1} in the last 30 seconds.",["pt"]="🔴 {0} eventos similares bloqueados de {1} nos últimos 30 segundos.",["fr"]="🔴 {0} événements similaires bloqués depuis {1} ces 30 dernières secondes.",["de"]="🔴 {0} ähnliche Ereignisse von {1} in den letzten 30 Sekunden blockiert.",["zh"]="🔴 过去30秒内已阻止来自 {1} 的 {0} 个类似事件。",["ru"]="🔴 За последние 30 секунд заблокировано {0} похожих событий от {1}.",["ja"]="🔴 過去30秒間に {1} から {0} 件の類似イベントをブロックしました。" },

        // ── Settings nav buttons (v2.0.5.3) ─────────────────────────────
        ["nav.dns"]      = new() { ["es"]="🌐  DNS",["en"]="🌐  DNS",["pt"]="🌐  DNS",["fr"]="🌐  DNS",["de"]="🌐  DNS",["zh"]="🌐  DNS",["ru"]="🌐  DNS",["ja"]="🌐  DNS" },
        ["nav.vault"]    = new() { ["es"]="🔑  Vault",["en"]="🔑  Vault",["pt"]="🔑  Vault",["fr"]="🔑  Coffre",["de"]="🔑  Tresor",["zh"]="🔑  密码库",["ru"]="🔑  Хранилище",["ja"]="🔑  保管庫" },
        ["nav.general"]  = new() { ["es"]="⚙️  General",["en"]="⚙️  General",["pt"]="⚙️  Geral",["fr"]="⚙️  Général",["de"]="⚙️  Allgemein",["zh"]="⚙️  常规",["ru"]="⚙️  Общие",["ja"]="⚙️  一般" },

        // ── Settings: Privacy section ────────────────────────────────────
        ["settings.privacy.title"]      = new() { ["es"]="Privacidad",["en"]="Privacy",["pt"]="Privacidade",["fr"]="Confidentialité",["de"]="Datenschutz",["zh"]="隐私",["ru"]="Конфиденциальность",["ja"]="プライバシー" },
        ["settings.security.label"]     = new() { ["es"]="Modo de seguridad",["en"]="Security mode",["pt"]="Modo de segurança",["fr"]="Mode de sécurité",["de"]="Sicherheitsmodus",["zh"]="安全模式",["ru"]="Режим безопасности",["ja"]="セキュリティモード" },
        ["settings.secmode.normal"]     = new() { ["es"]="Normal",["en"]="Normal",["pt"]="Normal",["fr"]="Normal",["de"]="Normal",["zh"]="普通",["ru"]="Обычный",["ja"]="標準" },
        ["settings.secmode.normal.desc"]= new() { ["es"]="Bloquea rastreadores y anuncios. Uso diario.",["en"]="Blocks trackers and ads. Daily use.",["pt"]="Bloqueia rastreadores e anúncios. Uso diário.",["fr"]="Bloque les traqueurs et les pubs. Usage quotidien.",["de"]="Blockiert Tracker und Werbung. Täglicher Gebrauch.",["zh"]="阻止跟踪器和广告。日常使用。",["ru"]="Блокирует трекеры и рекламу. Ежедневное использование.",["ja"]="トラッカーと広告をブロック。日常使用。" },
        ["settings.secmode.paranoid"]   = new() { ["es"]="Paranoid",["en"]="Paranoid",["pt"]="Paranoid",["fr"]="Paranoïaque",["de"]="Paranoid",["zh"]="偏执",["ru"]="Параноик",["ja"]="パラノイド" },
        ["settings.secmode.paranoid.desc"]= new() { ["es"]="Bloqueo agresivo. Historial borrado al salir.",["en"]="Aggressive blocking. History cleared on exit.",["pt"]="Bloqueio agressivo. Histórico apagado ao sair.",["fr"]="Blocage agressif. Historique effacé à la sortie.",["de"]="Aggressives Blockieren. Verlauf wird beim Beenden gelöscht.",["zh"]="激进阻止。退出时清除历史记录。",["ru"]="Агрессивная блокировка. История очищается при выходе.",["ja"]="積極的なブロック。終了時に履歴を削除。" },
        ["settings.secmode.bunker"]     = new() { ["es"]="Bunker",["en"]="Bunker",["pt"]="Bunker",["fr"]="Bunker",["de"]="Bunker",["zh"]="掩体",["ru"]="Бункер",["ja"]="バンカー" },
        ["settings.secmode.bunker.desc"]= new() { ["es"]="Máxima privacidad. Sin historial, sin cookies persistentes.",["en"]="Maximum privacy. No history, no persistent cookies.",["pt"]="Privacidade máxima. Sem histórico, sem cookies persistentes.",["fr"]="Confidentialité maximale. Pas d'historique, pas de cookies persistants.",["de"]="Maximale Privatsphäre. Kein Verlauf, keine permanenten Cookies.",["zh"]="最高隐私。无历史记录，无持久化Cookie。",["ru"]="Максимальная приватность. Без истории и постоянных файлов cookie.",["ja"]="最大限のプライバシー。履歴なし、永続的なCookieなし。" },
        ["settings.fp.title"]           = new() { ["es"]="Protección de huella digital",["en"]="Fingerprint protection",["pt"]="Proteção de impressão digital",["fr"]="Protection des empreintes",["de"]="Fingerabdruck-Schutz",["zh"]="指纹保护",["ru"]="Защита от отпечатка",["ja"]="フィンガープリント保護" },
        ["settings.fp.aggressive"]      = new() { ["es"]="Agresivo (Recomendado)",["en"]="Aggressive (Recommended)",["pt"]="Agressivo (Recomendado)",["fr"]="Agressif (Recommandé)",["de"]="Aggressiv (Empfohlen)",["zh"]="激进（推荐）",["ru"]="Агрессивный (рекомендуется)",["ja"]="積極的（推奨）" },
        ["settings.fp.aggressive.desc"] = new() { ["es"]="Canvas, WebGL, AudioContext, hardware spoof.",["en"]="Canvas, WebGL, AudioContext, hardware spoof.",["pt"]="Canvas, WebGL, AudioContext, spoof de hardware.",["fr"]="Canvas, WebGL, AudioContext, usurpation matérielle.",["de"]="Canvas, WebGL, AudioContext, Hardware-Spoofing.",["zh"]="Canvas、WebGL、AudioContext、硬件伪装。",["ru"]="Canvas, WebGL, AudioContext, подмена оборудования.",["ja"]="Canvas、WebGL、AudioContext、ハードウェアなりすまし。" },
        ["settings.fp.balanced"]        = new() { ["es"]="Balanceado",["en"]="Balanced",["pt"]="Balanceado",["fr"]="Équilibré",["de"]="Ausgewogen",["zh"]="平衡",["ru"]="Сбалансированный",["ja"]="バランス" },
        ["settings.fp.balanced.desc"]   = new() { ["es"]="Protección básica. Menos riesgo de romper sitios.",["en"]="Basic protection. Less risk of breaking sites.",["pt"]="Proteção básica. Menor risco de quebrar sites.",["fr"]="Protection de base. Moins de risque de casser des sites.",["de"]="Grundschutz. Geringeres Risiko, Websites zu beschädigen.",["zh"]="基础保护。破坏网站的风险较小。",["ru"]="Базовая защита. Меньше риск поломки сайтов.",["ja"]="基本保護。サイトが壊れるリスクが少ない。" },
        ["settings.fp.off"]             = new() { ["es"]="Desactivado",["en"]="Off",["pt"]="Desativado",["fr"]="Désactivé",["de"]="Aus",["zh"]="关闭",["ru"]="Отключено",["ja"]="オフ" },
        ["settings.webrtc.title"]       = new() { ["es"]="WebRTC",["en"]="WebRTC",["pt"]="WebRTC",["fr"]="WebRTC",["de"]="WebRTC",["zh"]="WebRTC",["ru"]="WebRTC",["ja"]="WebRTC" },
        ["settings.webrtc.relay"]       = new() { ["es"]="Solo relay (Recomendado)",["en"]="Relay only (Recommended)",["pt"]="Apenas relay (Recomendado)",["fr"]="Relais uniquement (Recommandé)",["de"]="Nur Relay (Empfohlen)",["zh"]="仅中继（推荐）",["ru"]="Только relay (рекомендуется)",["ja"]="リレーのみ（推奨）" },
        ["settings.webrtc.relay.desc"]  = new() { ["es"]="Oculta tu IP real usando solo servidores TURN.",["en"]="Hides your real IP using only TURN servers.",["pt"]="Oculta seu IP real usando apenas servidores TURN.",["fr"]="Masque votre IP réelle en utilisant uniquement des serveurs TURN.",["de"]="Verbirgt Ihre echte IP nur über TURN-Server.",["zh"]="仅使用TURN服务器隐藏您的真实IP。",["ru"]="Скрывает реальный IP, используя только серверы TURN.",["ja"]="TURNサーバーのみを使用して実際のIPを隠す。" },
        ["settings.webrtc.disabled"]    = new() { ["es"]="Desactivado",["en"]="Disabled",["pt"]="Desativado",["fr"]="Désactivé",["de"]="Deaktiviert",["zh"]="已禁用",["ru"]="Отключено",["ja"]="無効" },
        ["settings.webrtc.disabled.desc"]= new() { ["es"]="Elimina RTCPeerConnection. Puede romper videollamadas.",["en"]="Removes RTCPeerConnection. May break video calls.",["pt"]="Remove RTCPeerConnection. Pode quebrar chamadas de vídeo.",["fr"]="Supprime RTCPeerConnection. Peut casser les appels vidéo.",["de"]="Entfernt RTCPeerConnection. Kann Videoanrufe stören.",["zh"]="删除RTCPeerConnection。可能会中断视频通话。",["ru"]="Удаляет RTCPeerConnection. Может нарушить видеозвонки.",["ja"]="RTCPeerConnectionを削除。ビデオ通話が壊れる可能性。" },
        ["settings.webrtc.off"]         = new() { ["es"]="Sin protección",["en"]="No protection",["pt"]="Sem proteção",["fr"]="Aucune protection",["de"]="Kein Schutz",["zh"]="无保护",["ru"]="Без защиты",["ja"]="保護なし" },
        ["settings.history.save"]       = new() { ["es"]="Guardar historial de navegación",["en"]="Save browsing history",["pt"]="Salvar histórico de navegação",["fr"]="Enregistrer l'historique de navigation",["de"]="Browserverlauf speichern",["zh"]="保存浏览历史",["ru"]="Сохранять историю просмотров",["ja"]="閲覧履歴を保存" },
        ["settings.history.save.desc"]  = new() { ["es"]="Desactiva en modo Paranoid/Bunker automáticamente.",["en"]="Auto-disabled in Paranoid/Bunker mode.",["pt"]="Desativado automaticamente no modo Paranoid/Bunker.",["fr"]="Désactivé automatiquement en mode Paranoïaque/Bunker.",["de"]="Im Paranoid/Bunker-Modus automatisch deaktiviert.",["zh"]="在 Paranoid/Bunker 模式下自动禁用。",["ru"]="Автоматически отключается в режиме Paranoid/Bunker.",["ja"]="Paranoid/Bunkerモードで自動的に無効化。" },
        ["settings.history.clear"]      = new() { ["es"]="Borrar historial al salir",["en"]="Clear history on exit",["pt"]="Limpar histórico ao sair",["fr"]="Effacer l'historique à la sortie",["de"]="Verlauf beim Beenden löschen",["zh"]="退出时清除历史记录",["ru"]="Очищать историю при выходе",["ja"]="終了時に履歴を消去" },
        ["settings.history.clear.desc"] = new() { ["es"]="Siempre activo en modo Paranoid y Bunker.",["en"]="Always on in Paranoid and Bunker modes.",["pt"]="Sempre ativo no modo Paranoid e Bunker.",["fr"]="Toujours actif en mode Paranoïaque et Bunker.",["de"]="Im Paranoid- und Bunker-Modus immer aktiv.",["zh"]="在 Paranoid 和 Bunker 模式下始终启用。",["ru"]="Всегда активно в режимах Paranoid и Bunker.",["ja"]="ParanoidおよびBunkerモードでは常にオン。" },
        ["settings.update.auto"]        = new() { ["es"]="Buscar actualizaciones automáticamente",["en"]="Check for updates automatically",["pt"]="Verificar atualizações automaticamente",["fr"]="Rechercher automatiquement les mises à jour",["de"]="Automatisch nach Updates suchen",["zh"]="自动检查更新",["ru"]="Автоматически проверять обновления",["ja"]="自動的に更新を確認" },
        ["settings.update.auto.desc"]   = new() { ["es"]="VELO consultará GitHub cada 24h. Desactivado por defecto — la única conexión saliente que hace VELO sin tu acción.",["en"]="VELO will check GitHub every 24h. Off by default — the only outbound connection VELO makes without your action.",["pt"]="VELO consultará o GitHub a cada 24h. Desativado por padrão — a única conexão de saída que VELO faz sem sua ação.",["fr"]="VELO consultera GitHub toutes les 24h. Désactivé par défaut — la seule connexion sortante que VELO effectue sans votre action.",["de"]="VELO prüft alle 24h GitHub. Standardmäßig deaktiviert — die einzige ausgehende Verbindung, die VELO ohne Ihre Aktion herstellt.",["zh"]="VELO 将每 24 小时检查 GitHub。默认关闭 — 这是 VELO 在没有您操作时进行的唯一出站连接。",["ru"]="VELO будет проверять GitHub каждые 24 часа. По умолчанию отключено — единственное исходящее соединение, которое VELO делает без вашего действия.",["ja"]="VELOは24時間ごとにGitHubを確認します。デフォルトでオフ — VELOがあなたの操作なしで行う唯一の送信接続。" },

        // ── Settings: DNS section ─────────────────────────────────────────
        ["settings.dns.title"]          = new() { ["es"]="DNS Privado (DoH)",["en"]="Private DNS (DoH)",["pt"]="DNS Privado (DoH)",["fr"]="DNS Privé (DoH)",["de"]="Privates DNS (DoH)",["zh"]="私人 DNS (DoH)",["ru"]="Частный DNS (DoH)",["ja"]="プライベートDNS (DoH)" },
        ["settings.dns.intro"]          = new() { ["es"]="Tu ISP puede ver qué sitios visitas con DNS normal. VELO usa DNS encriptado sobre HTTPS.",["en"]="Your ISP can see what sites you visit with normal DNS. VELO uses encrypted DNS over HTTPS.",["pt"]="Seu provedor pode ver os sites que você visita com DNS normal. VELO usa DNS criptografado sobre HTTPS.",["fr"]="Votre FAI peut voir les sites que vous visitez avec un DNS normal. VELO utilise du DNS chiffré via HTTPS.",["de"]="Ihr Internetanbieter sieht Ihre besuchten Websites mit normalem DNS. VELO nutzt verschlüsseltes DNS über HTTPS.",["zh"]="使用普通 DNS 时，您的 ISP 可以看到您访问的网站。VELO 通过 HTTPS 使用加密 DNS。",["ru"]="С обычным DNS ваш провайдер видит, какие сайты вы посещаете. VELO использует зашифрованный DNS через HTTPS.",["ja"]="通常のDNSではISPが訪問サイトを見られます。VELOはHTTPS経由の暗号化DNSを使用します。" },
        ["settings.dns.quad9"]          = new() { ["es"]="Quad9 (Recomendado)",["en"]="Quad9 (Recommended)",["pt"]="Quad9 (Recomendado)",["fr"]="Quad9 (Recommandé)",["de"]="Quad9 (Empfohlen)",["zh"]="Quad9（推荐）",["ru"]="Quad9 (рекомендуется)",["ja"]="Quad9（推奨）" },
        ["settings.dns.quad9.desc"]     = new() { ["es"]="Sin logs · Bloquea malware · Con sede en Suiza",["en"]="No logs · Blocks malware · Based in Switzerland",["pt"]="Sem logs · Bloqueia malware · Sediado na Suíça",["fr"]="Sans logs · Bloque les malwares · Basé en Suisse",["de"]="Keine Protokolle · Blockiert Malware · Sitz in der Schweiz",["zh"]="无日志 · 阻止恶意软件 · 总部位于瑞士",["ru"]="Без логов · Блокирует вредоносное ПО · Базируется в Швейцарии",["ja"]="ログなし · マルウェアをブロック · スイスに拠点" },
        ["settings.dns.cloudflare.desc"]= new() { ["es"]="Ultra rápido · Política de privacidad limitada a 24h",["en"]="Ultra fast · Privacy policy limited to 24h",["pt"]="Ultra rápido · Política de privacidade limitada a 24h",["fr"]="Ultra rapide · Politique de confidentialité limitée à 24h",["de"]="Ultra schnell · Datenschutzrichtlinie auf 24h begrenzt",["zh"]="超快 · 隐私政策限24小时",["ru"]="Сверхбыстрый · Политика конфиденциальности на 24 ч",["ja"]="超高速 · プライバシーポリシーは24時間限定" },
        ["settings.dns.nextdns.desc"]   = new() { ["es"]="Filtrado personalizable · Requiere cuenta gratuita",["en"]="Customizable filtering · Requires free account",["pt"]="Filtragem personalizável · Requer conta gratuita",["fr"]="Filtrage personnalisable · Compte gratuit requis",["de"]="Anpassbares Filtern · Kostenloses Konto erforderlich",["zh"]="可自定义过滤 · 需要免费帐户",["ru"]="Настраиваемая фильтрация · Требуется бесплатный аккаунт",["ja"]="カスタマイズ可能なフィルタリング · 無料アカウントが必要" },
        ["settings.dns.custom"]         = new() { ["es"]="Personalizado",["en"]="Custom",["pt"]="Personalizado",["fr"]="Personnalisé",["de"]="Benutzerdefiniert",["zh"]="自定义",["ru"]="Пользовательский",["ja"]="カスタム" },
        ["settings.dns.url"]            = new() { ["es"]="URL DoH:",["en"]="DoH URL:",["pt"]="URL DoH:",["fr"]="URL DoH :",["de"]="DoH-URL:",["zh"]="DoH URL：",["ru"]="DoH URL:",["ja"]="DoH URL：" },

        // ── Settings: AI section ──────────────────────────────────────────
        ["settings.ai.title"]           = new() { ["es"]="Inteligencia Artificial",["en"]="Artificial Intelligence",["pt"]="Inteligência Artificial",["fr"]="Intelligence Artificielle",["de"]="Künstliche Intelligenz",["zh"]="人工智能",["ru"]="Искусственный интеллект",["ja"]="人工知能" },
        ["settings.ai.intro"]           = new() { ["es"]="VELO analiza URLs y recursos en tiempo real para detectar amenazas.",["en"]="VELO analyses URLs and resources in real time to detect threats.",["pt"]="VELO analisa URLs e recursos em tempo real para detectar ameaças.",["fr"]="VELO analyse les URL et ressources en temps réel pour détecter les menaces.",["de"]="VELO analysiert URLs und Ressourcen in Echtzeit, um Bedrohungen zu erkennen.",["zh"]="VELO 实时分析 URL 和资源以检测威胁。",["ru"]="VELO анализирует URL и ресурсы в реальном времени для обнаружения угроз.",["ja"]="VELOはURLとリソースをリアルタイムで分析し脅威を検出します。" },
        ["settings.ai.offline"]         = new() { ["es"]="Offline (Recomendado)",["en"]="Offline (Recommended)",["pt"]="Offline (Recomendado)",["fr"]="Hors ligne (Recommandé)",["de"]="Offline (Empfohlen)",["zh"]="离线（推荐）",["ru"]="Офлайн (рекомендуется)",["ja"]="オフライン（推奨）" },
        ["settings.ai.offline.desc"]    = new() { ["es"]="100% local. Sin API key. Reglas heurísticas.",["en"]="100% local. No API key. Heuristic rules.",["pt"]="100% local. Sem chave de API. Regras heurísticas.",["fr"]="100% local. Pas de clé API. Règles heuristiques.",["de"]="100% lokal. Kein API-Schlüssel. Heuristische Regeln.",["zh"]="100% 本地。无 API 密钥。启发式规则。",["ru"]="100% локально. Без API-ключа. Эвристические правила.",["ja"]="100%ローカル。APIキー不要。ヒューリスティックルール。" },
        ["settings.ai.claude"]          = new() { ["es"]="Claude (Mejor detección)",["en"]="Claude (Best detection)",["pt"]="Claude (Melhor detecção)",["fr"]="Claude (Meilleure détection)",["de"]="Claude (Beste Erkennung)",["zh"]="Claude（最佳检测）",["ru"]="Claude (лучшее обнаружение)",["ja"]="Claude（最高の検出）" },
        ["settings.ai.claude.desc"]     = new() { ["es"]="Requiere API key de Anthropic.",["en"]="Requires an Anthropic API key.",["pt"]="Requer chave de API da Anthropic.",["fr"]="Nécessite une clé API Anthropic.",["de"]="Erfordert einen Anthropic-API-Schlüssel.",["zh"]="需要 Anthropic API 密钥。",["ru"]="Требуется API-ключ Anthropic.",["ja"]="AnthropicのAPIキーが必要。" },
        ["settings.ai.apikey"]          = new() { ["es"]="API Key:",["en"]="API Key:",["pt"]="Chave API:",["fr"]="Clé API :",["de"]="API-Schlüssel:",["zh"]="API 密钥：",["ru"]="API-ключ:",["ja"]="APIキー：" },
        ["settings.ai.custom"]          = new() { ["es"]="LLM Personalizado (Ollama)",["en"]="Custom LLM (Ollama)",["pt"]="LLM Personalizado (Ollama)",["fr"]="LLM Personnalisé (Ollama)",["de"]="Benutzerdefiniertes LLM (Ollama)",["zh"]="自定义 LLM（Ollama）",["ru"]="Пользовательский LLM (Ollama)",["ja"]="カスタムLLM（Ollama）" },
        ["settings.ai.custom.desc"]     = new() { ["es"]="Cualquier endpoint compatible con OpenAI API.",["en"]="Any OpenAI-compatible API endpoint.",["pt"]="Qualquer endpoint compatível com a API do OpenAI.",["fr"]="Tout point d'accès compatible OpenAI API.",["de"]="Jeder OpenAI-kompatible API-Endpunkt.",["zh"]="任何兼容 OpenAI API 的端点。",["ru"]="Любой эндпоинт, совместимый с OpenAI API.",["ja"]="OpenAI API互換のエンドポイント。" },
        ["settings.ai.endpoint"]        = new() { ["es"]="Endpoint:",["en"]="Endpoint:",["pt"]="Endpoint:",["fr"]="Point d'accès :",["de"]="Endpunkt:",["zh"]="端点：",["ru"]="Эндпоинт:",["ja"]="エンドポイント：" },
        ["settings.ai.model"]           = new() { ["es"]="Modelo:",["en"]="Model:",["pt"]="Modelo:",["fr"]="Modèle :",["de"]="Modell:",["zh"]="模型：",["ru"]="Модель:",["ja"]="モデル：" },
        ["settings.ai.test"]            = new() { ["es"]="Probar conexión",["en"]="Test connection",["pt"]="Testar conexão",["fr"]="Tester la connexion",["de"]="Verbindung testen",["zh"]="测试连接",["ru"]="Проверить подключение",["ja"]="接続テスト" },
        ["settings.ai.testing"]         = new() { ["es"]="Probando…",["en"]="Testing…",["pt"]="Testando…",["fr"]="Test en cours…",["de"]="Teste…",["zh"]="测试中…",["ru"]="Проверка…",["ja"]="テスト中…" },
        ["settings.ai.loading_model"]   = new() { ["es"]="Cargando modelo…",["en"]="Loading model…",["pt"]="Carregando modelo…",["fr"]="Chargement du modèle…",["de"]="Modell wird geladen…",["zh"]="加载模型中…",["ru"]="Загрузка модели…",["ja"]="モデルを読み込み中…" },
        ["settings.ai.fill_first"]      = new() { ["es"]="Completa el endpoint y el modelo antes de probar.",["en"]="Fill in the endpoint and model before testing.",["pt"]="Preencha o endpoint e o modelo antes de testar.",["fr"]="Remplissez le point d'accès et le modèle avant de tester.",["de"]="Endpunkt und Modell vor dem Testen ausfüllen.",["zh"]="测试前请填写端点和模型。",["ru"]="Заполните эндпоинт и модель перед проверкой.",["ja"]="テスト前にエンドポイントとモデルを入力してください。" },
        ["settings.ai.server_error"]    = new() { ["es"]="El servidor responde en {0} pero con error {1}.\nComprueba que el servidor esté iniciado y el endpoint sea correcto (p.ej. http://127.0.0.1:11434 para Ollama, http://127.0.0.1:1234 para LM Studio).",["en"]="Server responds at {0} but with error {1}.\nCheck the server is running and the endpoint is correct (e.g. http://127.0.0.1:11434 for Ollama, http://127.0.0.1:1234 for LM Studio).",["pt"]="O servidor responde em {0} mas com erro {1}.\nVerifique se o servidor está iniciado e o endpoint está correto (ex.: http://127.0.0.1:11434 para Ollama, http://127.0.0.1:1234 para LM Studio).",["fr"]="Le serveur répond sur {0} mais avec l'erreur {1}.\nVérifiez que le serveur est démarré et que le point d'accès est correct (par ex. http://127.0.0.1:11434 pour Ollama, http://127.0.0.1:1234 pour LM Studio).",["de"]="Server antwortet auf {0}, aber mit Fehler {1}.\nPrüfen Sie, ob der Server läuft und der Endpunkt korrekt ist (z. B. http://127.0.0.1:11434 für Ollama, http://127.0.0.1:1234 für LM Studio).",["zh"]="服务器在 {0} 响应但有错误 {1}。\n请检查服务器是否已启动，端点是否正确（例如 http://127.0.0.1:11434 用于 Ollama，http://127.0.0.1:1234 用于 LM Studio）。",["ru"]="Сервер отвечает на {0}, но с ошибкой {1}.\nПроверьте, запущен ли сервер и корректен ли эндпоинт (например, http://127.0.0.1:11434 для Ollama, http://127.0.0.1:1234 для LM Studio).",["ja"]="サーバーは {0} で応答しますがエラー {1} です。\nサーバーが起動しており、エンドポイントが正しいか確認してください（例：Ollamaは http://127.0.0.1:11434 、LM Studioは http://127.0.0.1:1234 ）。" },
        ["settings.ai.model_missing"]   = new() { ["es"]="Servidor activo ✓, pero el modelo '{0}' no aparece en la lista.\nAsegúrate de haber cargado el modelo en LM Studio o ejecuta: ollama pull {0}",["en"]="Server active ✓, but model '{0}' is not in the list.\nMake sure you loaded the model in LM Studio, or run: ollama pull {0}",["pt"]="Servidor ativo ✓, mas o modelo '{0}' não aparece na lista.\nCertifique-se de ter carregado o modelo no LM Studio ou execute: ollama pull {0}",["fr"]="Serveur actif ✓, mais le modèle '{0}' n'est pas dans la liste.\nAssurez-vous d'avoir chargé le modèle dans LM Studio ou exécutez : ollama pull {0}",["de"]="Server aktiv ✓, aber Modell '{0}' ist nicht in der Liste.\nStellen Sie sicher, dass das Modell in LM Studio geladen ist, oder führen Sie aus: ollama pull {0}",["zh"]="服务器活动 ✓，但模型 '{0}' 不在列表中。\n请确保您已在 LM Studio 中加载模型，或运行：ollama pull {0}",["ru"]="Сервер активен ✓, но модель '{0}' отсутствует в списке.\nУбедитесь, что модель загружена в LM Studio, или выполните: ollama pull {0}",["ja"]="サーバーはアクティブ ✓ ですが、モデル '{0}' がリストにありません。\nLM Studioでモデルがロードされていることを確認するか、実行：ollama pull {0}" },
        ["settings.ai.cant_connect"]    = new() { ["es"]="No se pudo conectar a {0}.\n• Ollama: abre una terminal y ejecuta ollama serve\n• LM Studio: activa el servidor local desde la pestaña Developer",["en"]="Could not connect to {0}.\n• Ollama: open a terminal and run ollama serve\n• LM Studio: enable the local server from the Developer tab",["pt"]="Não foi possível conectar a {0}.\n• Ollama: abra um terminal e execute ollama serve\n• LM Studio: ative o servidor local na aba Developer",["fr"]="Impossible de se connecter à {0}.\n• Ollama : ouvrez un terminal et exécutez ollama serve\n• LM Studio : activez le serveur local depuis l'onglet Developer",["de"]="Verbindung zu {0} fehlgeschlagen.\n• Ollama: Terminal öffnen und ollama serve ausführen\n• LM Studio: Lokalen Server im Developer-Tab aktivieren",["zh"]="无法连接到 {0}。\n• Ollama：打开终端运行 ollama serve\n• LM Studio：从 Developer 选项卡启用本地服务器",["ru"]="Не удалось подключиться к {0}.\n• Ollama: откройте терминал и выполните ollama serve\n• LM Studio: включите локальный сервер во вкладке Developer",["ja"]="{0} に接続できません。\n• Ollama：ターミナルを開き ollama serve を実行\n• LM Studio：Developerタブからローカルサーバーを有効化" },
        ["settings.ai.http_error"]      = new() { ["es"]="Error HTTP {0} al generar respuesta con '{1}'.",["en"]="HTTP error {0} while generating a response with '{1}'.",["pt"]="Erro HTTP {0} ao gerar resposta com '{1}'.",["fr"]="Erreur HTTP {0} lors de la génération d'une réponse avec '{1}'.",["de"]="HTTP-Fehler {0} beim Generieren einer Antwort mit '{1}'.",["zh"]="使用 '{1}' 生成响应时出现 HTTP 错误 {0}。",["ru"]="HTTP-ошибка {0} при генерации ответа с '{1}'.",["ja"]="'{1}' で応答生成中にHTTPエラー {0} 。" },
        ["settings.ai.success"]         = new() { ["es"]="✓ Conectado · Modelo '{0}' listo\nTiempo de respuesta: {1} ms\nRespuesta: {2}",["en"]="✓ Connected · Model '{0}' ready\nResponse time: {1} ms\nReply: {2}",["pt"]="✓ Conectado · Modelo '{0}' pronto\nTempo de resposta: {1} ms\nResposta: {2}",["fr"]="✓ Connecté · Modèle '{0}' prêt\nTemps de réponse : {1} ms\nRéponse : {2}",["de"]="✓ Verbunden · Modell '{0}' bereit\nAntwortzeit: {1} ms\nAntwort: {2}",["zh"]="✓ 已连接 · 模型 '{0}' 就绪\n响应时间：{1} ms\n回复：{2}",["ru"]="✓ Подключено · Модель '{0}' готова\nВремя ответа: {1} мс\nОтвет: {2}",["ja"]="✓ 接続済み · モデル '{0}' 準備完了\n応答時間：{1} ms\n応答：{2}" },
        ["settings.ai.timeout"]         = new() { ["es"]="Timeout (120s) — el modelo tardó demasiado en responder.\nSi usas un modelo grande, asegúrate de que esté cargado en LM Studio.\nTambién puedes probar con un modelo más pequeño.",["en"]="Timeout (120s) — model took too long to respond.\nIf you use a large model, make sure it's loaded in LM Studio.\nYou can also try a smaller model.",["pt"]="Timeout (120s) — o modelo demorou demais para responder.\nSe você usa um modelo grande, garanta que esteja carregado no LM Studio.\nTambém pode tentar um modelo menor.",["fr"]="Délai (120s) — le modèle a mis trop de temps à répondre.\nSi vous utilisez un grand modèle, assurez-vous qu'il soit chargé dans LM Studio.\nVous pouvez aussi essayer un modèle plus petit.",["de"]="Zeitüberschreitung (120s) — das Modell brauchte zu lange.\nBei großem Modell sicherstellen, dass es in LM Studio geladen ist.\nAlternativ ein kleineres Modell ausprobieren.",["zh"]="超时（120秒）— 模型响应时间过长。\n如果使用大型模型，请确保已在 LM Studio 中加载。\n您也可以尝试更小的模型。",["ru"]="Таймаут (120с) — модель слишком долго отвечала.\nЕсли вы используете большую модель, убедитесь, что она загружена в LM Studio.\nТакже можно попробовать модель поменьше.",["ja"]="タイムアウト（120秒）— モデルの応答に時間がかかりすぎました。\n大きなモデルを使用する場合は、LM Studioにロードされているか確認してください。\nより小さなモデルを試すこともできます。" },
        ["settings.ai.unexpected"]      = new() { ["es"]="Error inesperado: {0}",["en"]="Unexpected error: {0}",["pt"]="Erro inesperado: {0}",["fr"]="Erreur inattendue : {0}",["de"]="Unerwarteter Fehler: {0}",["zh"]="意外错误：{0}",["ru"]="Неожиданная ошибка: {0}",["ja"]="予期しないエラー：{0}" },

        // ── Settings: Search section ──────────────────────────────────────
        ["settings.search.title"]       = new() { ["es"]="Motor de búsqueda",["en"]="Search engine",["pt"]="Motor de busca",["fr"]="Moteur de recherche",["de"]="Suchmaschine",["zh"]="搜索引擎",["ru"]="Поисковая система",["ja"]="検索エンジン" },
        ["settings.search.ddg"]         = new() { ["es"]="DuckDuckGo (Recomendado)",["en"]="DuckDuckGo (Recommended)",["pt"]="DuckDuckGo (Recomendado)",["fr"]="DuckDuckGo (Recommandé)",["de"]="DuckDuckGo (Empfohlen)",["zh"]="DuckDuckGo（推荐）",["ru"]="DuckDuckGo (рекомендуется)",["ja"]="DuckDuckGo（推奨）" },
        ["settings.search.ddg.desc"]    = new() { ["es"]="Sin rastreo · Sin perfil de usuario",["en"]="No tracking · No user profile",["pt"]="Sem rastreamento · Sem perfil de usuário",["fr"]="Sans suivi · Sans profil utilisateur",["de"]="Kein Tracking · Kein Nutzerprofil",["zh"]="无跟踪 · 无用户画像",["ru"]="Без отслеживания · Без профиля пользователя",["ja"]="追跡なし · ユーザープロファイルなし" },
        ["settings.search.brave.desc"]  = new() { ["es"]="Índice propio · Sin Google",["en"]="Own index · No Google",["pt"]="Índice próprio · Sem Google",["fr"]="Index propre · Pas de Google",["de"]="Eigener Index · Kein Google",["zh"]="自有索引 · 无 Google",["ru"]="Собственный индекс · Без Google",["ja"]="独自のインデックス · Googleなし" },
        ["settings.search.searx.desc"]  = new() { ["es"]="Meta-buscador open source · Auto-hospedado",["en"]="Open source meta-search · Self-hosted",["pt"]="Meta-buscador open source · Auto-hospedado",["fr"]="Méta-moteur open source · Auto-hébergé",["de"]="Open-Source-Meta-Suche · Selbst gehostet",["zh"]="开源元搜索 · 自托管",["ru"]="Метапоисковик с открытым исходным кодом · Самостоятельный хостинг",["ja"]="オープンソースのメタ検索 · セルフホスト" },
        ["settings.search.url_hint"]    = new() { ["es"]="Usa {{query}} como marcador. Ej: https://mi-searx.com/search?q={{query}}",["en"]="Use {{query}} as the placeholder. e.g. https://my-searx.com/search?q={{query}}",["pt"]="Use {{query}} como marcador. Ex.: https://meu-searx.com/search?q={{query}}",["fr"]="Utilisez {{query}} comme espace réservé. Ex. : https://mon-searx.com/search?q={{query}}",["de"]="Verwenden Sie {{query}} als Platzhalter. z. B. https://mein-searx.com/search?q={{query}}",["zh"]="使用 {{query}} 作为占位符。例如：https://my-searx.com/search?q={{query}}",["ru"]="Используйте {{query}} как заполнитель. Пример: https://my-searx.com/search?q={{query}}",["ja"]="プレースホルダとして {{query}} を使用。例：https://my-searx.com/search?q={{query}}" },

        // ── Settings: Vault section (panel inside Settings, not VaultWindow)
        ["settings.vault.intro"]        = new() { ["es"]="Gestor de contraseñas encriptado con AES-256-GCM. La master password nunca se almacena.",["en"]="Password manager encrypted with AES-256-GCM. The master password is never stored.",["pt"]="Gerenciador de senhas criptografado com AES-256-GCM. A master password nunca é armazenada.",["fr"]="Gestionnaire de mots de passe chiffré avec AES-256-GCM. Le mot de passe maître n'est jamais stocké.",["de"]="Mit AES-256-GCM verschlüsselter Passwort-Manager. Das Master-Passwort wird nie gespeichert.",["zh"]="使用 AES-256-GCM 加密的密码管理器。永不存储主密码。",["ru"]="Менеджер паролей с шифрованием AES-256-GCM. Мастер-пароль никогда не сохраняется.",["ja"]="AES-256-GCMで暗号化されたパスワードマネージャー。マスターパスワードは保存されません。" },
        ["settings.vault.autolock"]     = new() { ["es"]="Bloqueo automático",["en"]="Auto-lock",["pt"]="Bloqueio automático",["fr"]="Verrouillage automatique",["de"]="Automatisch sperren",["zh"]="自动锁定",["ru"]="Автоблокировка",["ja"]="自動ロック" },
        ["settings.vault.5m"]           = new() { ["es"]="5 minutos",["en"]="5 minutes",["pt"]="5 minutos",["fr"]="5 minutes",["de"]="5 Minuten",["zh"]="5 分钟",["ru"]="5 минут",["ja"]="5分" },
        ["settings.vault.10m"]          = new() { ["es"]="10 minutos",["en"]="10 minutes",["pt"]="10 minutos",["fr"]="10 minutes",["de"]="10 Minuten",["zh"]="10 分钟",["ru"]="10 минут",["ja"]="10分" },
        ["settings.vault.15m"]          = new() { ["es"]="15 minutos",["en"]="15 minutes",["pt"]="15 minutos",["fr"]="15 minutes",["de"]="15 Minuten",["zh"]="15 分钟",["ru"]="15 минут",["ja"]="15分" },
        ["settings.vault.30m"]          = new() { ["es"]="30 minutos",["en"]="30 minutes",["pt"]="30 minutos",["fr"]="30 minutes",["de"]="30 Minuten",["zh"]="30 分钟",["ru"]="30 минут",["ja"]="30分" },
        ["settings.vault.1h"]           = new() { ["es"]="1 hora",["en"]="1 hour",["pt"]="1 hora",["fr"]="1 heure",["de"]="1 Stunde",["zh"]="1 小时",["ru"]="1 час",["ja"]="1時間" },
        ["settings.vault.never"]        = new() { ["es"]="Nunca",["en"]="Never",["pt"]="Nunca",["fr"]="Jamais",["de"]="Nie",["zh"]="从不",["ru"]="Никогда",["ja"]="なし" },
        ["settings.vault.changepass"]   = new() { ["es"]="Cambiar master password",["en"]="Change master password",["pt"]="Alterar master password",["fr"]="Changer le mot de passe maître",["de"]="Master-Passwort ändern",["zh"]="更改主密码",["ru"]="Изменить мастер-пароль",["ja"]="マスターパスワードを変更" },
        ["settings.vault.newpass"]      = new() { ["es"]="Nueva master password:",["en"]="New master password:",["pt"]="Nova master password:",["fr"]="Nouveau mot de passe maître :",["de"]="Neues Master-Passwort:",["zh"]="新主密码：",["ru"]="Новый мастер-пароль:",["ja"]="新しいマスターパスワード：" },
        ["settings.vault.confirm"]      = new() { ["es"]="Confirmar:",["en"]="Confirm:",["pt"]="Confirmar:",["fr"]="Confirmer :",["de"]="Bestätigen:",["zh"]="确认：",["ru"]="Подтвердить:",["ja"]="確認：" },
        ["settings.vault.changebtn"]    = new() { ["es"]="Cambiar password",["en"]="Change password",["pt"]="Alterar senha",["fr"]="Changer le mot de passe",["de"]="Passwort ändern",["zh"]="更改密码",["ru"]="Сменить пароль",["ja"]="パスワード変更" },
        ["settings.vault.min8"]         = new() { ["es"]="Mínimo 8 caracteres.",["en"]="Minimum 8 characters.",["pt"]="Mínimo 8 caracteres.",["fr"]="Minimum 8 caractères.",["de"]="Mindestens 8 Zeichen.",["zh"]="至少 8 个字符。",["ru"]="Минимум 8 символов.",["ja"]="最低8文字。" },
        ["settings.vault.mismatch"]     = new() { ["es"]="Las passwords no coinciden.",["en"]="Passwords do not match.",["pt"]="As senhas não coincidem.",["fr"]="Les mots de passe ne correspondent pas.",["de"]="Passwörter stimmen nicht überein.",["zh"]="密码不匹配。",["ru"]="Пароли не совпадают.",["ja"]="パスワードが一致しません。" },
        ["settings.vault.updated"]      = new() { ["es"]="✓ Master password actualizada",["en"]="✓ Master password updated",["pt"]="✓ Master password atualizada",["fr"]="✓ Mot de passe maître mis à jour",["de"]="✓ Master-Passwort aktualisiert",["zh"]="✓ 主密码已更新",["ru"]="✓ Мастер-пароль обновлён",["ja"]="✓ マスターパスワードを更新しました" },

        // ── Settings: General section ─────────────────────────────────────
        ["settings.general.title"]      = new() { ["es"]="General",["en"]="General",["pt"]="Geral",["fr"]="Général",["de"]="Allgemein",["zh"]="常规",["ru"]="Общие",["ja"]="一般" },
        ["settings.defbrowser.title"]   = new() { ["es"]="Navegador predeterminado",["en"]="Default browser",["pt"]="Navegador padrão",["fr"]="Navigateur par défaut",["de"]="Standardbrowser",["zh"]="默认浏览器",["ru"]="Браузер по умолчанию",["ja"]="既定のブラウザ" },
        ["settings.defbrowser.help"]    = new() { ["es"]="VELO se abrirá cuando hagas clic en enlaces desde email, apps y documentos. Windows 11 requiere que confirmes el cambio en Configuración del sistema.",["en"]="VELO will open when you click links from email, apps and documents. Windows 11 requires you to confirm the change in System Settings.",["pt"]="VELO abrirá quando você clicar em links de e-mails, apps e documentos. Windows 11 exige que você confirme a mudança nas Configurações do sistema.",["fr"]="VELO s'ouvrira lorsque vous cliquerez sur des liens depuis des e-mails, applications et documents. Windows 11 nécessite que vous confirmiez le changement dans les Paramètres système.",["de"]="VELO öffnet sich, wenn Sie auf Links aus E-Mails, Apps und Dokumenten klicken. Windows 11 verlangt, dass Sie die Änderung in den Systemeinstellungen bestätigen.",["zh"]="当您从电子邮件、应用和文档单击链接时，VELO 将打开。Windows 11 要求您在系统设置中确认更改。",["ru"]="VELO будет открываться при щелчке по ссылкам из почты, приложений и документов. Windows 11 требует подтвердить изменение в Системных настройках.",["ja"]="メール、アプリ、ドキュメントからリンクをクリックするとVELOが開きます。Windows 11ではシステム設定で変更を確認する必要があります。" },
        ["settings.defbrowser.btn"]     = new() { ["es"]="Establecer VELO como navegador predeterminado",["en"]="Set VELO as default browser",["pt"]="Definir VELO como navegador padrão",["fr"]="Définir VELO comme navigateur par défaut",["de"]="VELO als Standardbrowser festlegen",["zh"]="将 VELO 设为默认浏览器",["ru"]="Сделать VELO браузером по умолчанию",["ja"]="VELOを既定のブラウザに設定" },
        ["settings.defbrowser.openerr"] = new() { ["es"]="No se pudo abrir Configuración de Windows: {0}",["en"]="Could not open Windows Settings: {0}",["pt"]="Não foi possível abrir as Configurações do Windows: {0}",["fr"]="Impossible d'ouvrir les Paramètres Windows : {0}",["de"]="Windows-Einstellungen konnten nicht geöffnet werden: {0}",["zh"]="无法打开 Windows 设置：{0}",["ru"]="Не удалось открыть параметры Windows: {0}",["ja"]="Windows設定を開けませんでした：{0}" },

        // ── Settings: Save / Cancel / Status ──────────────────────────────
        ["settings.save"]               = new() { ["es"]="Guardar",["en"]="Save",["pt"]="Salvar",["fr"]="Enregistrer",["de"]="Speichern",["zh"]="保存",["ru"]="Сохранить",["ja"]="保存" },
        ["settings.cancel"]             = new() { ["es"]="Cancelar",["en"]="Cancel",["pt"]="Cancelar",["fr"]="Annuler",["de"]="Abbrechen",["zh"]="取消",["ru"]="Отмена",["ja"]="キャンセル" },
        ["settings.saved"]              = new() { ["es"]="✓ Guardado",["en"]="✓ Saved",["pt"]="✓ Salvo",["fr"]="✓ Enregistré",["de"]="✓ Gespeichert",["zh"]="✓ 已保存",["ru"]="✓ Сохранено",["ja"]="✓ 保存しました" },

        // ── Onboarding wizard ─────────────────────────────────────────────
        ["onboarding.title"]            = new() { ["es"]="VELO — Configuración inicial",["en"]="VELO — Initial setup",["pt"]="VELO — Configuração inicial",["fr"]="VELO — Configuration initiale",["de"]="VELO — Ersteinrichtung",["zh"]="VELO — 初始设置",["ru"]="VELO — Первоначальная настройка",["ja"]="VELO — 初期設定" },
        ["onboarding.step"]             = new() { ["es"]="Paso {0} de {1}",["en"]="Step {0} of {1}",["pt"]="Passo {0} de {1}",["fr"]="Étape {0} sur {1}",["de"]="Schritt {0} von {1}",["zh"]="步骤 {0}/{1}",["ru"]="Шаг {0} из {1}",["ja"]="ステップ {0}/{1}" },
        ["onboarding.back"]             = new() { ["es"]="← Atrás",["en"]="← Back",["pt"]="← Voltar",["fr"]="← Retour",["de"]="← Zurück",["zh"]="← 返回",["ru"]="← Назад",["ja"]="← 戻る" },
        ["onboarding.next"]             = new() { ["es"]="Continuar →",["en"]="Continue →",["pt"]="Continuar →",["fr"]="Continuer →",["de"]="Weiter →",["zh"]="继续 →",["ru"]="Продолжить →",["ja"]="続行 →" },
        ["onboarding.start"]            = new() { ["es"]="Empezar →",["en"]="Get started →",["pt"]="Começar →",["fr"]="Commencer →",["de"]="Loslegen →",["zh"]="开始 →",["ru"]="Начать →",["ja"]="開始 →" },
        ["onboarding.s1.title"]         = new() { ["es"]="Elige cómo VELO analiza amenazas",["en"]="Choose how VELO analyses threats",["pt"]="Escolha como o VELO analisa ameaças",["fr"]="Choisissez comment VELO analyse les menaces",["de"]="Wählen Sie, wie VELO Bedrohungen analysiert",["zh"]="选择 VELO 分析威胁的方式",["ru"]="Выберите, как VELO анализирует угрозы",["ja"]="VELOの脅威分析方法を選択" },
        ["onboarding.s1.offline.desc"]  = new() { ["es"]="100% local. Sin API key. Seguro para uso diario.",["en"]="100% local. No API key. Safe for daily use.",["pt"]="100% local. Sem chave de API. Seguro para uso diário.",["fr"]="100% local. Pas de clé API. Sûr pour un usage quotidien.",["de"]="100% lokal. Kein API-Schlüssel. Sicher für den täglichen Gebrauch.",["zh"]="100% 本地。无 API 密钥。日常使用安全。",["ru"]="100% локально. Без API-ключа. Безопасно для повседневного использования.",["ja"]="100%ローカル。APIキー不要。日常使用に安全。" },
        ["onboarding.s1.claude.desc"]   = new() { ["es"]="Requiere API key de Anthropic. Análisis más profundo.",["en"]="Requires Anthropic API key. Deeper analysis.",["pt"]="Requer chave de API da Anthropic. Análise mais profunda.",["fr"]="Nécessite une clé API Anthropic. Analyse approfondie.",["de"]="Erfordert Anthropic-API-Schlüssel. Tiefergehende Analyse.",["zh"]="需要 Anthropic API 密钥。更深入的分析。",["ru"]="Требуется API-ключ Anthropic. Более глубокий анализ.",["ja"]="AnthropicのAPIキーが必要。より詳細な分析。" },
        ["onboarding.s1.custom"]        = new() { ["es"]="LLM Personalizado",["en"]="Custom LLM",["pt"]="LLM Personalizado",["fr"]="LLM Personnalisé",["de"]="Benutzerdefiniertes LLM",["zh"]="自定义 LLM",["ru"]="Пользовательский LLM",["ja"]="カスタムLLM" },
        ["onboarding.s1.custom.desc"]   = new() { ["es"]="Ollama u otro endpoint compatible.",["en"]="Ollama or another compatible endpoint.",["pt"]="Ollama ou outro endpoint compatível.",["fr"]="Ollama ou un autre point d'accès compatible.",["de"]="Ollama oder ein anderer kompatibler Endpunkt.",["zh"]="Ollama 或其他兼容端点。",["ru"]="Ollama или другой совместимый эндпоинт.",["ja"]="Ollamaまたは他の互換エンドポイント。" },
        ["onboarding.s2.title"]         = new() { ["es"]="DNS Privado",["en"]="Private DNS",["pt"]="DNS Privado",["fr"]="DNS Privé",["de"]="Privates DNS",["zh"]="私人 DNS",["ru"]="Частный DNS",["ja"]="プライベートDNS" },
        ["onboarding.s2.intro"]         = new() { ["es"]="Tu ISP puede ver qué sitios visitas con DNS normal. VELO usa DNS encriptado (DoH).",["en"]="Your ISP can see what sites you visit with normal DNS. VELO uses encrypted DNS (DoH).",["pt"]="Seu provedor pode ver os sites que você visita com DNS normal. VELO usa DNS criptografado (DoH).",["fr"]="Votre FAI peut voir les sites que vous visitez avec un DNS normal. VELO utilise du DNS chiffré (DoH).",["de"]="Ihr Internetanbieter sieht Ihre besuchten Websites mit normalem DNS. VELO verwendet verschlüsseltes DNS (DoH).",["zh"]="使用普通 DNS 时您的 ISP 可以看到您访问的网站。VELO 使用加密 DNS (DoH)。",["ru"]="С обычным DNS ваш провайдер видит, какие сайты вы посещаете. VELO использует зашифрованный DNS (DoH).",["ja"]="通常のDNSではISPが訪問サイトを見られます。VELOは暗号化DNS (DoH) を使用します。" },
        ["onboarding.s2.quad9.desc"]    = new() { ["es"]="Sin logs · Bloquea malware · Suiza",["en"]="No logs · Blocks malware · Switzerland",["pt"]="Sem logs · Bloqueia malware · Suíça",["fr"]="Sans logs · Bloque les malwares · Suisse",["de"]="Keine Protokolle · Blockiert Malware · Schweiz",["zh"]="无日志 · 阻止恶意软件 · 瑞士",["ru"]="Без логов · Блокирует вредоносное ПО · Швейцария",["ja"]="ログなし · マルウェアをブロック · スイス" },
        ["onboarding.s3.title"]         = new() { ["es"]="Protección de Identidad",["en"]="Identity Protection",["pt"]="Proteção de Identidade",["fr"]="Protection de l'identité",["de"]="Identitätsschutz",["zh"]="身份保护",["ru"]="Защита личности",["ja"]="アイデンティティ保護" },
        ["onboarding.s3.intro"]         = new() { ["es"]="Los sitios pueden identificarte sin cookies usando tu 'huella digital' del navegador.",["en"]="Sites can identify you without cookies using your browser 'fingerprint'.",["pt"]="Os sites podem identificá-lo sem cookies usando sua 'impressão digital' do navegador.",["fr"]="Les sites peuvent vous identifier sans cookies grâce à l'« empreinte » de votre navigateur.",["de"]="Websites können Sie ohne Cookies anhand Ihres Browser-Fingerabdrucks identifizieren.",["zh"]="网站可以使用您浏览器的“指纹”在没有 Cookie 的情况下识别您。",["ru"]="Сайты могут идентифицировать вас без cookie, используя «отпечаток» браузера.",["ja"]="サイトはCookieなしでブラウザの「指紋」を使ってあなたを識別できます。" },
        ["onboarding.s3.already"]       = new() { ["es"]="VELO ya tiene activada:",["en"]="VELO already has enabled:",["pt"]="VELO já tem ativado:",["fr"]="VELO a déjà activé :",["de"]="VELO hat bereits aktiviert:",["zh"]="VELO 已启用：",["ru"]="VELO уже активировал:",["ja"]="VELOで既に有効：" },
        ["onboarding.s3.canvas"]        = new() { ["es"]="✓ Protección Canvas",["en"]="✓ Canvas protection",["pt"]="✓ Proteção Canvas",["fr"]="✓ Protection Canvas",["de"]="✓ Canvas-Schutz",["zh"]="✓ Canvas 保护",["ru"]="✓ Защита Canvas",["ja"]="✓ Canvas保護" },
        ["onboarding.s3.hardware"]      = new() { ["es"]="✓ Spoof de hardware",["en"]="✓ Hardware spoof",["pt"]="✓ Spoof de hardware",["fr"]="✓ Usurpation matérielle",["de"]="✓ Hardware-Spoof",["zh"]="✓ 硬件伪装",["ru"]="✓ Подмена оборудования",["ja"]="✓ ハードウェアなりすまし" },
        ["onboarding.s3.ua"]            = new() { ["es"]="✓ Rotación de User Agent",["en"]="✓ User-Agent rotation",["pt"]="✓ Rotação de User Agent",["fr"]="✓ Rotation du User-Agent",["de"]="✓ User-Agent-Rotation",["zh"]="✓ User-Agent 轮换",["ru"]="✓ Ротация User-Agent",["ja"]="✓ User-Agentローテーション" },
        ["onboarding.s3.webrtc"]        = new() { ["es"]="✓ Protección WebRTC",["en"]="✓ WebRTC protection",["pt"]="✓ Proteção WebRTC",["fr"]="✓ Protection WebRTC",["de"]="✓ WebRTC-Schutz",["zh"]="✓ WebRTC 保护",["ru"]="✓ Защита WebRTC",["ja"]="✓ WebRTC保護" },
        ["onboarding.s3.level"]         = new() { ["es"]="Nivel: AGRESIVO (óptimo)",["en"]="Level: AGGRESSIVE (optimal)",["pt"]="Nível: AGRESSIVO (ótimo)",["fr"]="Niveau : AGRESSIF (optimal)",["de"]="Stufe: AGGRESSIV (optimal)",["zh"]="级别：激进（最佳）",["ru"]="Уровень: АГРЕССИВНЫЙ (оптимальный)",["ja"]="レベル：積極的（最適）" },
        ["onboarding.s3.changeable"]    = new() { ["es"]="Puedes cambiarlo en Settings en cualquier momento.",["en"]="You can change it in Settings at any time.",["pt"]="Você pode alterá-lo em Configurações a qualquer momento.",["fr"]="Vous pouvez le modifier dans les Paramètres à tout moment.",["de"]="Sie können es jederzeit in den Einstellungen ändern.",["zh"]="您可以随时在设置中更改它。",["ru"]="Вы можете изменить это в Настройках в любое время.",["ja"]="いつでも設定で変更できます。" },
        ["onboarding.s4.title"]         = new() { ["es"]="Password Vault",["en"]="Password Vault",["pt"]="Cofre de Senhas",["fr"]="Coffre-fort",["de"]="Passwort-Tresor",["zh"]="密码库",["ru"]="Хранилище паролей",["ja"]="パスワード保管庫" },
        ["onboarding.s4.intro"]         = new() { ["es"]="VELO incluye un gestor de contraseñas encriptado. Elige una master password para protegerlo.",["en"]="VELO includes an encrypted password manager. Choose a master password to protect it.",["pt"]="VELO inclui um gerenciador de senhas criptografado. Escolha uma master password para protegê-lo.",["fr"]="VELO inclut un gestionnaire de mots de passe chiffré. Choisissez un mot de passe maître pour le protéger.",["de"]="VELO enthält einen verschlüsselten Passwort-Manager. Wählen Sie ein Master-Passwort zum Schutz.",["zh"]="VELO 包括一个加密的密码管理器。选择一个主密码来保护它。",["ru"]="В VELO встроен зашифрованный менеджер паролей. Выберите мастер-пароль для его защиты.",["ja"]="VELOには暗号化されたパスワードマネージャーが含まれています。保護するためにマスターパスワードを選択してください。" },
        ["onboarding.s4.master"]        = new() { ["es"]="Master password (mínimo 8 caracteres):",["en"]="Master password (minimum 8 characters):",["pt"]="Master password (mínimo 8 caracteres):",["fr"]="Mot de passe maître (minimum 8 caractères) :",["de"]="Master-Passwort (mindestens 8 Zeichen):",["zh"]="主密码（至少 8 个字符）：",["ru"]="Мастер-пароль (минимум 8 символов):",["ja"]="マスターパスワード（最低8文字）：" },
        ["onboarding.s4.confirm"]       = new() { ["es"]="Confirmar password:",["en"]="Confirm password:",["pt"]="Confirmar senha:",["fr"]="Confirmer le mot de passe :",["de"]="Passwort bestätigen:",["zh"]="确认密码：",["ru"]="Подтвердить пароль:",["ja"]="パスワード確認：" },
        ["onboarding.s4.min8"]          = new() { ["es"]="La master password debe tener al menos 8 caracteres.",["en"]="The master password must be at least 8 characters.",["pt"]="A master password deve ter pelo menos 8 caracteres.",["fr"]="Le mot de passe maître doit comporter au moins 8 caractères.",["de"]="Das Master-Passwort muss mindestens 8 Zeichen lang sein.",["zh"]="主密码必须至少 8 个字符。",["ru"]="Мастер-пароль должен содержать не менее 8 символов.",["ja"]="マスターパスワードは8文字以上必要です。" },
        ["onboarding.s4.mismatch"]      = new() { ["es"]="Las passwords no coinciden.",["en"]="Passwords do not match.",["pt"]="As senhas não coincidem.",["fr"]="Les mots de passe ne correspondent pas.",["de"]="Passwörter stimmen nicht überein.",["zh"]="密码不匹配。",["ru"]="Пароли не совпадают.",["ja"]="パスワードが一致しません。" },

        // ── Find bar (Ctrl+F) ─────────────────────────────────────────────
        ["find.label"]                  = new() { ["es"]="Buscar:",["en"]="Find:",["pt"]="Buscar:",["fr"]="Rechercher :",["de"]="Suchen:",["zh"]="查找：",["ru"]="Найти:",["ja"]="検索：" },
        ["find.prev"]                   = new() { ["es"]="Anterior (Shift+Enter)",["en"]="Previous (Shift+Enter)",["pt"]="Anterior (Shift+Enter)",["fr"]="Précédent (Shift+Entrée)",["de"]="Zurück (Shift+Enter)",["zh"]="上一个（Shift+Enter）",["ru"]="Назад (Shift+Enter)",["ja"]="前へ（Shift+Enter）" },
        ["find.next"]                   = new() { ["es"]="Siguiente (Enter)",["en"]="Next (Enter)",["pt"]="Próximo (Enter)",["fr"]="Suivant (Entrée)",["de"]="Weiter (Enter)",["zh"]="下一个（Enter）",["ru"]="Дальше (Enter)",["ja"]="次へ（Enter）" },
        ["find.close"]                  = new() { ["es"]="Cerrar (Esc)",["en"]="Close (Esc)",["pt"]="Fechar (Esc)",["fr"]="Fermer (Échap)",["de"]="Schließen (Esc)",["zh"]="关闭（Esc）",["ru"]="Закрыть (Esc)",["ja"]="閉じる（Esc）" },
        ["find.notfound"]               = new() { ["es"]="No encontrado",["en"]="Not found",["pt"]="Não encontrado",["fr"]="Introuvable",["de"]="Nicht gefunden",["zh"]="未找到",["ru"]="Не найдено",["ja"]="見つかりません" },

        // ── External-protocol dialogs (BrowserTab) ───────────────────────
        ["ext.protocol.title"]          = new() { ["es"]="VELO — Protocolo externo",["en"]="VELO — External protocol",["pt"]="VELO — Protocolo externo",["fr"]="VELO — Protocole externe",["de"]="VELO — Externes Protokoll",["zh"]="VELO — 外部协议",["ru"]="VELO — Внешний протокол",["ja"]="VELO — 外部プロトコル" },
        ["ext.protocol.prompt"]         = new() { ["es"]="Una página quiere abrir una aplicación externa con el protocolo:\n\n    {0}://\n\nURI completo: {1}\n\n¿Permitir? (Esta decisión se recuerda hasta que cierres VELO.)",["en"]="A page wants to open an external application with the protocol:\n\n    {0}://\n\nFull URI: {1}\n\nAllow? (This decision is remembered until you close VELO.)",["pt"]="Uma página quer abrir um aplicativo externo com o protocolo:\n\n    {0}://\n\nURI completo: {1}\n\nPermitir? (Esta decisão é lembrada até você fechar o VELO.)",["fr"]="Une page souhaite ouvrir une application externe avec le protocole :\n\n    {0}://\n\nURI complet : {1}\n\nAutoriser ? (Cette décision est mémorisée jusqu'à la fermeture de VELO.)",["de"]="Eine Seite möchte eine externe Anwendung mit dem Protokoll öffnen:\n\n    {0}://\n\nVollständiger URI: {1}\n\nErlauben? (Diese Entscheidung wird bis zum Schließen von VELO gespeichert.)",["zh"]="某个页面想用以下协议打开外部应用：\n\n    {0}://\n\n完整 URI：{1}\n\n允许？（此决定会保留到您关闭 VELO。）",["ru"]="Страница хочет открыть внешнее приложение с протоколом:\n\n    {0}://\n\nПолный URI: {1}\n\nРазрешить? (Это решение запомнится до закрытия VELO.)",["ja"]="ページが次のプロトコルで外部アプリを開こうとしています：\n\n    {0}://\n\n完全なURI：{1}\n\n許可しますか？（VELOを閉じるまで記憶されます。）" },
        ["ext.protocol.fail"]           = new() { ["es"]="No se pudo abrir el protocolo '{0}://'.\n\nPosiblemente la aplicación no está instalada o no está registrada para este protocolo.",["en"]="Could not open protocol '{0}://'.\n\nThe application is likely not installed or not registered for this protocol.",["pt"]="Não foi possível abrir o protocolo '{0}://'.\n\nProvavelmente o aplicativo não está instalado ou registrado para este protocolo.",["fr"]="Impossible d'ouvrir le protocole '{0}://'.\n\nL'application n'est probablement pas installée ou non enregistrée pour ce protocole.",["de"]="Protokoll '{0}://' konnte nicht geöffnet werden.\n\nDie Anwendung ist wahrscheinlich nicht installiert oder nicht für dieses Protokoll registriert.",["zh"]="无法打开协议 '{0}://'。\n\n可能未安装该应用程序或未为此协议注册。",["ru"]="Не удалось открыть протокол '{0}://'.\n\nВозможно, приложение не установлено или не зарегистрировано для этого протокола.",["ja"]="プロトコル '{0}://' を開けませんでした。\n\nアプリがインストールされていないか、このプロトコルに登録されていない可能性があります。" },

        // ── Vault status (status bar messages) ────────────────────────────
        ["vault.entries.count"]         = new() { ["es"]="({0} entradas)",["en"]="({0} entries)",["pt"]="({0} entradas)",["fr"]="({0} entrées)",["de"]="({0} Einträge)",["zh"]="（{0} 条）",["ru"]="({0} записей)",["ja"]="（{0}件）" },
        ["vault.saved"]                 = new() { ["es"]="✓ Entrada guardada",["en"]="✓ Entry saved",["pt"]="✓ Entrada salva",["fr"]="✓ Entrée enregistrée",["de"]="✓ Eintrag gespeichert",["zh"]="✓ 已保存条目",["ru"]="✓ Запись сохранена",["ja"]="✓ エントリを保存しました" },
        ["vault.updated"]               = new() { ["es"]="✓ Entrada actualizada",["en"]="✓ Entry updated",["pt"]="✓ Entrada atualizada",["fr"]="✓ Entrée mise à jour",["de"]="✓ Eintrag aktualisiert",["zh"]="✓ 已更新条目",["ru"]="✓ Запись обновлена",["ja"]="✓ エントリを更新しました" },
        ["vault.deleted"]               = new() { ["es"]="✓ Entrada eliminada",["en"]="✓ Entry deleted",["pt"]="✓ Entrada excluída",["fr"]="✓ Entrée supprimée",["de"]="✓ Eintrag gelöscht",["zh"]="✓ 已删除条目",["ru"]="✓ Запись удалена",["ja"]="✓ エントリを削除しました" },
        ["vault.delete.confirm"]        = new() { ["es"]="¿Eliminar la entrada de {0}?",["en"]="Delete the entry for {0}?",["pt"]="Excluir a entrada de {0}?",["fr"]="Supprimer l'entrée de {0} ?",["de"]="Eintrag für {0} löschen?",["zh"]="删除 {0} 的条目？",["ru"]="Удалить запись для {0}?",["ja"]="{0} のエントリを削除しますか？" },
        ["vault.delete.confirm.title"]  = new() { ["es"]="VELO — Confirmar",["en"]="VELO — Confirm",["pt"]="VELO — Confirmar",["fr"]="VELO — Confirmer",["de"]="VELO — Bestätigen",["zh"]="VELO — 确认",["ru"]="VELO — Подтверждение",["ja"]="VELO — 確認" },

        // ── UrlBar dynamic strings (loading/AI status/bookmark) ──────────
        ["urlbar.stop"]                 = new() { ["es"]="Detener",["en"]="Stop",["pt"]="Parar",["fr"]="Arrêter",["de"]="Stopp",["zh"]="停止",["ru"]="Стоп",["ja"]="停止" },
        ["urlbar.zoom.reset"]           = new() { ["es"]="Restablecer zoom (Ctrl+0)",["en"]="Reset zoom (Ctrl+0)",["pt"]="Redefinir zoom (Ctrl+0)",["fr"]="Réinitialiser le zoom (Ctrl+0)",["de"]="Zoom zurücksetzen (Strg+0)",["zh"]="重置缩放（Ctrl+0）",["ru"]="Сбросить масштаб (Ctrl+0)",["ja"]="ズームをリセット（Ctrl+0）" },
        ["urlbar.bookmark.add"]         = new() { ["es"]="Guardar marcador",["en"]="Add bookmark",["pt"]="Adicionar favorito",["fr"]="Ajouter un favori",["de"]="Lesezeichen hinzufügen",["zh"]="添加书签",["ru"]="Добавить в закладки",["ja"]="ブックマークを追加" },
        ["urlbar.bookmark.remove"]      = new() { ["es"]="Eliminar marcador",["en"]="Remove bookmark",["pt"]="Remover favorito",["fr"]="Supprimer le favori",["de"]="Lesezeichen entfernen",["zh"]="移除书签",["ru"]="Удалить из закладок",["ja"]="ブックマークを削除" },
        ["urlbar.ai.ready"]             = new() { ["es"]="IA activa · {0}\nAnalizando amenazas en tiempo real",["en"]="AI active · {0}\nAnalysing threats in real time",["pt"]="IA ativa · {0}\nAnalisando ameaças em tempo real",["fr"]="IA active · {0}\nAnalyse des menaces en temps réel",["de"]="KI aktiv · {0}\nBedrohungen werden in Echtzeit analysiert",["zh"]="AI 活动 · {0}\n实时分析威胁",["ru"]="ИИ активен · {0}\nАнализ угроз в реальном времени",["ja"]="AI動作中 · {0}\nリアルタイムで脅威を分析" },
        ["urlbar.ai.connecting"]        = new() { ["es"]="IA conectando…",["en"]="AI connecting…",["pt"]="IA conectando…",["fr"]="IA en connexion…",["de"]="KI verbindet…",["zh"]="AI 连接中…",["ru"]="ИИ подключается…",["ja"]="AI接続中…" },
        ["urlbar.ai.error"]             = new() { ["es"]="IA no disponible · {0}\nRevisa que Ollama esté corriendo: ollama serve",["en"]="AI unavailable · {0}\nMake sure Ollama is running: ollama serve",["pt"]="IA indisponível · {0}\nVerifique se o Ollama está rodando: ollama serve",["fr"]="IA indisponible · {0}\nVérifiez qu'Ollama est en cours d'exécution : ollama serve",["de"]="KI nicht verfügbar · {0}\nStellen Sie sicher, dass Ollama läuft: ollama serve",["zh"]="AI 不可用 · {0}\n请确认 Ollama 正在运行：ollama serve",["ru"]="ИИ недоступен · {0}\nУбедитесь, что Ollama запущен: ollama serve",["ja"]="AI利用不可 · {0}\nOllamaが起動しているか確認：ollama serve" },
        ["urlbar.ai.offline"]           = new() { ["es"]="IA offline · Análisis heurístico local activo",["en"]="AI offline · Local heuristic analysis active",["pt"]="IA offline · Análise heurística local ativa",["fr"]="IA hors ligne · Analyse heuristique locale active",["de"]="KI offline · Lokale heuristische Analyse aktiv",["zh"]="AI 离线 · 本地启发式分析已启用",["ru"]="ИИ офлайн · Локальный эвристический анализ активен",["ja"]="AIオフライン · ローカルヒューリスティック分析が有効" },

        // ── TabSidebar / Downloads tooltips / History badges / Inspector buttons / Malwaredex empty
        ["sidebar.aria"]                = new() { ["es"]="Barra lateral de pestañas",["en"]="Tab sidebar",["pt"]="Barra lateral de abas",["fr"]="Barre latérale d'onglets",["de"]="Tab-Seitenleiste",["zh"]="标签侧边栏",["ru"]="Боковая панель вкладок",["ja"]="タブのサイドバー" },
        ["downloads.open"]              = new() { ["es"]="Abrir / ejecutar archivo",["en"]="Open / run file",["pt"]="Abrir / executar arquivo",["fr"]="Ouvrir / exécuter le fichier",["de"]="Datei öffnen / ausführen",["zh"]="打开 / 运行文件",["ru"]="Открыть / запустить файл",["ja"]="ファイルを開く / 実行" },
        ["downloads.folder"]            = new() { ["es"]="Abrir carpeta",["en"]="Open folder",["pt"]="Abrir pasta",["fr"]="Ouvrir le dossier",["de"]="Ordner öffnen",["zh"]="打开文件夹",["ru"]="Открыть папку",["ja"]="フォルダを開く" },
        ["downloads.remove"]            = new() { ["es"]="Quitar de la lista",["en"]="Remove from list",["pt"]="Remover da lista",["fr"]="Retirer de la liste",["de"]="Aus Liste entfernen",["zh"]="从列表中移除",["ru"]="Убрать из списка",["ja"]="リストから削除" },
        ["history.badge.blocked"]       = new() { ["es"]="bloqueados",["en"]="blocked",["pt"]="bloqueados",["fr"]="bloqués",["de"]="blockiert",["zh"]="已阻止",["ru"]="заблокировано",["ja"]="ブロック済み" },
        ["history.badge.trackers"]      = new() { ["es"]="rastreadores",["en"]="trackers",["pt"]="rastreadores",["fr"]="traqueurs",["de"]="Tracker",["zh"]="跟踪器",["ru"]="трекеры",["ja"]="トラッカー" },
        ["history.badge.malware"]       = new() { ["es"]="malware",["en"]="malware",["pt"]="malware",["fr"]="malware",["de"]="Malware",["zh"]="恶意软件",["ru"]="вредоносное ПО",["ja"]="マルウェア" },
        ["history.no_threats"]          = new() { ["es"]="✓ Sin amenazas",["en"]="✓ No threats",["pt"]="✓ Sem ameaças",["fr"]="✓ Aucune menace",["de"]="✓ Keine Bedrohungen",["zh"]="✓ 无威胁",["ru"]="✓ Без угроз",["ja"]="✓ 脅威なし" },
        ["mdx.loading"]                 = new() { ["es"]="Cargando…",["en"]="Loading…",["pt"]="Carregando…",["fr"]="Chargement…",["de"]="Lädt…",["zh"]="加载中…",["ru"]="Загрузка…",["ja"]="読み込み中…" },
        ["mdx.empty.title"]             = new() { ["es"]="Ninguna amenaza capturada todavía",["en"]="No threats captured yet",["pt"]="Nenhuma ameaça capturada ainda",["fr"]="Aucune menace capturée pour le moment",["de"]="Noch keine Bedrohungen erfasst",["zh"]="尚未捕获任何威胁",["ru"]="Угрозы пока не обнаружены",["ja"]="まだ脅威は捕捉されていません" },
        ["mdx.empty.desc"]              = new() { ["es"]="Navega en la web y VELO registrará aquí cada tipo de amenaza bloqueada.",["en"]="Browse the web and VELO will record each type of blocked threat here.",["pt"]="Navegue na web e VELO registrará aqui cada tipo de ameaça bloqueada.",["fr"]="Naviguez sur le web et VELO enregistrera ici chaque type de menace bloquée.",["de"]="Surfen Sie im Web und VELO erfasst hier jede blockierte Bedrohungsart.",["zh"]="浏览网页时，VELO 将在此处记录每种被阻止的威胁类型。",["ru"]="Просматривайте веб, и VELO будет фиксировать здесь каждый тип заблокированных угроз.",["ja"]="ウェブを閲覧すると、VELOがブロックした各種の脅威をここに記録します。" },
        ["mdx.legend"]                  = new() { ["es"]="★☆☆ Primera captura · ★★☆ Evolución · ★★★ Forma Final",["en"]="★☆☆ First capture · ★★☆ Evolution · ★★★ Final Form",["pt"]="★☆☆ Primeira captura · ★★☆ Evolução · ★★★ Forma Final",["fr"]="★☆☆ Première capture · ★★☆ Évolution · ★★★ Forme finale",["de"]="★☆☆ Erste Erfassung · ★★☆ Evolution · ★★★ Endform",["zh"]="★☆☆ 首次捕获 · ★★☆ 进化 · ★★★ 最终形态",["ru"]="★☆☆ Первый захват · ★★☆ Эволюция · ★★★ Финальная форма",["ja"]="★☆☆ 初捕獲 · ★★☆ 進化 · ★★★ 最終形態" },
        ["inspector.aria"]              = new() { ["es"]="Análisis de seguridad y privacidad del sitio activo",["en"]="Security and privacy analysis of the active site",["pt"]="Análise de segurança e privacidade do site ativo",["fr"]="Analyse de sécurité et de confidentialité du site actif",["de"]="Sicherheits- und Datenschutzanalyse der aktiven Website",["zh"]="活动站点的安全与隐私分析",["ru"]="Анализ безопасности и конфиденциальности активного сайта",["ja"]="アクティブサイトのセキュリティとプライバシー分析" },

        // ── DownloadGuard reasons (v2.0.5.5) ──────────────────────────────
        ["download.block.burst"]        = new() { ["es"]="Descarga bloqueada: múltiples descargas automáticas detectadas (ataque drive-by). El sitio intentó descargar '{0}' sin tu permiso.",["en"]="Download blocked: multiple automatic downloads detected (drive-by attack). The site tried to download '{0}' without your permission.",["pt"]="Download bloqueado: múltiplos downloads automáticos detectados (ataque drive-by). O site tentou baixar '{0}' sem sua permissão.",["fr"]="Téléchargement bloqué : plusieurs téléchargements automatiques détectés (attaque drive-by). Le site a tenté de télécharger '{0}' sans votre permission.",["de"]="Download blockiert: mehrere automatische Downloads erkannt (Drive-by-Angriff). Die Seite versuchte '{0}' ohne Ihre Erlaubnis herunterzuladen.",["zh"]="下载已阻止：检测到多个自动下载（路过式攻击）。网站试图未经您许可下载 '{0}'。",["ru"]="Загрузка заблокирована: обнаружено несколько автоматических загрузок (drive-by атака). Сайт пытался загрузить '{0}' без вашего разрешения.",["ja"]="ダウンロードをブロック：複数の自動ダウンロードを検出（ドライブバイ攻撃）。サイトが '{0}' を許可なくダウンロードしようとしました。" },
        ["download.block.crossorigin"]  = new() { ["es"]="Descarga bloqueada: '{0}' es un ejecutable descargado desde un dominio diferente al de la página. Este patrón es característico de malware drive-by.",["en"]="Download blocked: '{0}' is an executable from a domain different from the page. This pattern is typical of drive-by malware.",["pt"]="Download bloqueado: '{0}' é um executável de um domínio diferente do da página. Esse padrão é típico de malware drive-by.",["fr"]="Téléchargement bloqué : '{0}' est un exécutable provenant d'un domaine différent de la page. Ce schéma est typique des malwares drive-by.",["de"]="Download blockiert: '{0}' ist eine ausführbare Datei von einer anderen Domain als der Seite. Dieses Muster ist typisch für Drive-by-Malware.",["zh"]="下载已阻止：'{0}' 是来自与页面不同域的可执行文件。此模式是路过式恶意软件的典型特征。",["ru"]="Загрузка заблокирована: '{0}' — это исполняемый файл из другого домена, чем страница. Такой шаблон характерен для drive-by вредоносного ПО.",["ja"]="ダウンロードをブロック：'{0}' はページとは異なるドメインの実行ファイルです。このパターンはドライブバイ型マルウェアによく見られます。" },
        ["download.warn.dangerous"]     = new() { ["es"]="'{0}' es un archivo ejecutable. Asegúrate de que confías en este sitio antes de abrirlo.",["en"]="'{0}' is an executable file. Make sure you trust this site before opening it.",["pt"]="'{0}' é um arquivo executável. Garanta que você confia neste site antes de abri-lo.",["fr"]="'{0}' est un fichier exécutable. Assurez-vous de faire confiance à ce site avant de l'ouvrir.",["de"]="'{0}' ist eine ausführbare Datei. Stellen Sie sicher, dass Sie dieser Website vertrauen, bevor Sie sie öffnen.",["zh"]="'{0}' 是可执行文件。打开前请确保您信任此网站。",["ru"]="'{0}' — исполняемый файл. Убедитесь, что вы доверяете этому сайту, прежде чем открыть его.",["ja"]="'{0}' は実行ファイルです。開く前にこのサイトを信頼できるか確認してください。" },

        // ── About page (v2.0.5.10) ────────────────────────────────────────
        ["about.title"]                 = new() { ["es"]="Acerca de VELO",["en"]="About VELO",["pt"]="Sobre o VELO",["fr"]="À propos de VELO",["de"]="Über VELO",["zh"]="关于 VELO",["ru"]="О VELO",["ja"]="VELOについて" },
        ["about.unsigned.header"]       = new() { ["es"]="⚠ Build no firmado",["en"]="⚠ Unsigned build",["pt"]="⚠ Build não assinado",["fr"]="⚠ Build non signé",["de"]="⚠ Unsignierter Build",["zh"]="⚠ 未签名构建",["ru"]="⚠ Сборка без подписи",["ja"]="⚠ 未署名ビルド" },
        ["about.unsigned.body"]         = new() { ["es"]="Este binario no tiene firma Authenticode. Windows SmartScreen mostrará una advertencia. Verifica la integridad con el hash SHA256 publicado en la página de Releases:",["en"]="This binary is not Authenticode-signed. Windows SmartScreen will show a warning. Verify integrity against the SHA256 hash published on the Releases page:",["pt"]="Este binário não está assinado com Authenticode. O Windows SmartScreen mostrará um aviso. Verifique a integridade com o hash SHA256 publicado na página de Releases:",["fr"]="Ce binaire n'est pas signé avec Authenticode. Windows SmartScreen affichera un avertissement. Vérifiez l'intégrité avec le hash SHA256 publié sur la page Releases :",["de"]="Diese Binärdatei ist nicht Authenticode-signiert. Windows SmartScreen zeigt eine Warnung an. Überprüfen Sie die Integrität anhand des auf der Releases-Seite veröffentlichten SHA256-Hashes:",["zh"]="此二进制文件未使用 Authenticode 签名。Windows SmartScreen 会显示警告。请使用 Releases 页面上发布的 SHA256 哈希验证完整性：",["ru"]="Этот бинарник не подписан Authenticode. Windows SmartScreen покажет предупреждение. Проверьте целостность по хешу SHA256 со страницы Releases:",["ja"]="このバイナリはAuthenticode署名されていません。Windows SmartScreenが警告を表示します。Releasesページで公開されているSHA256ハッシュで整合性を確認してください：" },
        ["about.builtwith"]             = new() { ["es"]="Construido con C# · .NET 8 · WPF · Microsoft WebView2",["en"]="Built with C# · .NET 8 · WPF · Microsoft WebView2",["pt"]="Construído com C# · .NET 8 · WPF · Microsoft WebView2",["fr"]="Construit avec C# · .NET 8 · WPF · Microsoft WebView2",["de"]="Gebaut mit C# · .NET 8 · WPF · Microsoft WebView2",["zh"]="使用 C# · .NET 8 · WPF · Microsoft WebView2 构建",["ru"]="Создано с C# · .NET 8 · WPF · Microsoft WebView2",["ja"]="C# · .NET 8 · WPF · Microsoft WebView2 で構築" },

        // ── NewTab stats (v2.0.5.10) ──────────────────────────────────────
        ["newtab.stats.trackers"]       = new() { ["es"]="{0} rastreadores bloqueados",["en"]="{0} trackers blocked",["pt"]="{0} rastreadores bloqueados",["fr"]="{0} traqueurs bloqués",["de"]="{0} Tracker blockiert",["zh"]="已阻止 {0} 个跟踪器",["ru"]="{0} трекеров заблокировано",["ja"]="{0} 件のトラッカーをブロック" },
        ["newtab.stats.requests"]       = new() { ["es"]="{0} requests bloqueados",["en"]="{0} requests blocked",["pt"]="{0} solicitações bloqueadas",["fr"]="{0} requêtes bloquées",["de"]="{0} Anfragen blockiert",["zh"]="已阻止 {0} 个请求",["ru"]="{0} запросов заблокировано",["ja"]="{0} 件のリクエストをブロック" },
        ["newtab.stats.sites"]          = new() { ["es"]="{0} sitios visitados",["en"]="{0} sites visited",["pt"]="{0} sites visitados",["fr"]="{0} sites visités",["de"]="{0} besuchte Seiten",["zh"]="已访问 {0} 个网站",["ru"]="{0} сайтов посещено",["ja"]="{0} サイトを訪問" },
        ["newtab.stats.total"]          = new() { ["es"]=" en total",["en"]=" in total",["pt"]=" no total",["fr"]=" au total",["de"]=" insgesamt",["zh"]="（总计）",["ru"]=" всего",["ja"]=" 合計" },

        // ── Tab close (v2.0.5.12) — context-menu item for sidebar ────────
        ["sidebar.tab.close"]           = new() { ["es"]="Cerrar pestaña",["en"]="Close tab",["pt"]="Fechar aba",["fr"]="Fermer l'onglet",["de"]="Tab schließen",["zh"]="关闭标签页",["ru"]="Закрыть вкладку",["ja"]="タブを閉じる" },

        // ── Threats Panel v3 (Phase 3 Sprint 1) ──────────────────────────
        ["threatspanel.header.one"]     = new() { ["es"]="Amenazas — {0} bloqueo",["en"]="Threats — {0} block",["pt"]="Ameaças — {0} bloqueio",["fr"]="Menaces — {0} blocage",["de"]="Bedrohungen — {0} blockiert",["zh"]="威胁 — 已阻止 {0} 个",["ru"]="Угрозы — {0} блокировка",["ja"]="脅威 — {0} 件をブロック" },
        ["threatspanel.header.many"]    = new() { ["es"]="Amenazas — {0} bloqueos",["en"]="Threats — {0} blocks",["pt"]="Ameaças — {0} bloqueios",["fr"]="Menaces — {0} blocages",["de"]="Bedrohungen — {0} blockiert",["zh"]="威胁 — 已阻止 {0} 个",["ru"]="Угрозы — {0} блокировок",["ja"]="脅威 — {0} 件をブロック" },
        ["threatspanel.summary.empty"]  = new() { ["es"]="Sin amenazas en esta pestaña.",["en"]="No threats on this tab.",["pt"]="Sem ameaças nesta aba.",["fr"]="Aucune menace sur cet onglet.",["de"]="Keine Bedrohungen auf diesem Tab.",["zh"]="此标签页无威胁。",["ru"]="Нет угроз на этой вкладке.",["ja"]="このタブに脅威はありません。" },
        ["threatspanel.summary.trackers"]    = new() { ["es"]="Trackers: {0}",["en"]="Trackers: {0}",["pt"]="Rastreadores: {0}",["fr"]="Traqueurs : {0}",["de"]="Tracker: {0}",["zh"]="跟踪器：{0}",["ru"]="Трекеры: {0}",["ja"]="トラッカー：{0}" },
        ["threatspanel.summary.malware"]     = new() { ["es"]="Malware: {0}",["en"]="Malware: {0}",["pt"]="Malware: {0}",["fr"]="Malware : {0}",["de"]="Malware: {0}",["zh"]="恶意软件：{0}",["ru"]="Вредоносное ПО: {0}",["ja"]="マルウェア：{0}" },
        ["threatspanel.summary.ads"]         = new() { ["es"]="Anuncios: {0}",["en"]="Ads: {0}",["pt"]="Anúncios: {0}",["fr"]="Publicités : {0}",["de"]="Werbung: {0}",["zh"]="广告：{0}",["ru"]="Реклама: {0}",["ja"]="広告：{0}" },
        ["threatspanel.summary.fingerprint"] = new() { ["es"]="Fingerprint: {0}",["en"]="Fingerprint: {0}",["pt"]="Fingerprint: {0}",["fr"]="Empreinte : {0}",["de"]="Fingerprint: {0}",["zh"]="指纹：{0}",["ru"]="Отпечаток: {0}",["ja"]="フィンガープリント：{0}" },
        ["threatspanel.explain.loading"] = new() { ["es"]="Generando explicación…",["en"]="Generating explanation…",["pt"]="Gerando explicação…",["fr"]="Génération de l'explication…",["de"]="Erkläre…",["zh"]="生成解释中…",["ru"]="Генерируется объяснение…",["ja"]="説明を生成中…" },
        ["threatspanel.explain.error"]   = new() { ["es"]="Error generando explicación: {0}",["en"]="Error generating explanation: {0}",["pt"]="Erro ao gerar explicação: {0}",["fr"]="Erreur de génération : {0}",["de"]="Fehler bei der Erklärung: {0}",["zh"]="生成解释时出错：{0}",["ru"]="Ошибка генерации объяснения: {0}",["ja"]="説明生成エラー：{0}" },
        ["threatspanel.export.ok"]       = new() { ["es"]="Sesión exportada a {0}",["en"]="Session exported to {0}",["pt"]="Sessão exportada para {0}",["fr"]="Session exportée vers {0}",["de"]="Sitzung exportiert nach {0}",["zh"]="会话已导出到 {0}",["ru"]="Сессия экспортирована в {0}",["ja"]="セッションを {0} にエクスポートしました" },
        ["threatspanel.export.error"]    = new() { ["es"]="Error exportando: {0}",["en"]="Export error: {0}",["pt"]="Erro ao exportar: {0}",["fr"]="Erreur d'exportation : {0}",["de"]="Exportfehler: {0}",["zh"]="导出错误：{0}",["ru"]="Ошибка экспорта: {0}",["ja"]="エクスポートエラー：{0}" },

        // ── Context Menu IA (Phase 3 Sprint 1E) ──────────────────────────
        ["ctx.ai.menu"]                  = new() { ["es"]="🤖 IA",["en"]="🤖 AI",["pt"]="🤖 IA",["fr"]="🤖 IA",["de"]="🤖 KI",["zh"]="🤖 AI",["ru"]="🤖 ИИ",["ja"]="🤖 AI" },
        ["ctx.ai.text.explain"]          = new() { ["es"]="💬 Explicar selección",["en"]="💬 Explain selection",["pt"]="💬 Explicar seleção",["fr"]="💬 Expliquer la sélection",["de"]="💬 Auswahl erklären",["zh"]="💬 解释选中内容",["ru"]="💬 Объяснить выделенное",["ja"]="💬 選択範囲を説明" },
        ["ctx.ai.text.summarize"]        = new() { ["es"]="📝 Resumir en 3 líneas",["en"]="📝 Summarise in 3 lines",["pt"]="📝 Resumir em 3 linhas",["fr"]="📝 Résumer en 3 lignes",["de"]="📝 In 3 Zeilen zusammenfassen",["zh"]="📝 用3行总结",["ru"]="📝 Резюмировать в 3 строках",["ja"]="📝 3行で要約" },
        ["ctx.ai.text.translate"]        = new() { ["es"]="🌐 Traducir",["en"]="🌐 Translate",["pt"]="🌐 Traduzir",["fr"]="🌐 Traduire",["de"]="🌐 Übersetzen",["zh"]="🌐 翻译",["ru"]="🌐 Перевести",["ja"]="🌐 翻訳" },
        ["ctx.ai.text.factcheck"]        = new() { ["es"]="✅ Verificar hecho",["en"]="✅ Fact-check",["pt"]="✅ Verificar fato",["fr"]="✅ Vérifier le fait",["de"]="✅ Faktencheck",["zh"]="✅ 事实核查",["ru"]="✅ Проверить факт",["ja"]="✅ ファクトチェック" },
        ["ctx.ai.text.define"]           = new() { ["es"]="📚 Definir palabra/concepto",["en"]="📚 Define word/concept",["pt"]="📚 Definir palavra/conceito",["fr"]="📚 Définir le mot/concept",["de"]="📚 Wort/Begriff definieren",["zh"]="📚 定义词语",["ru"]="📚 Определить слово",["ja"]="📚 用語を定義" },
        ["ctx.ai.text.eli5"]             = new() { ["es"]="🎯 Explicar como si tuviera 5 años",["en"]="🎯 Explain like I'm 5",["pt"]="🎯 Explicar como se eu tivesse 5 anos",["fr"]="🎯 Expliquer comme à un enfant de 5 ans",["de"]="🎯 Erkläre als wäre ich 5",["zh"]="🎯 像对5岁小孩解释",["ru"]="🎯 Объяснить простыми словами",["ja"]="🎯 5歳児にもわかるように" },
        ["ctx.ai.text.extract.links"]    = new() { ["es"]="🔗 Extraer enlaces",["en"]="🔗 Extract links",["pt"]="🔗 Extrair links",["fr"]="🔗 Extraire les liens",["de"]="🔗 Links extrahieren",["zh"]="🔗 提取链接",["ru"]="🔗 Извлечь ссылки",["ja"]="🔗 リンクを抽出" },
        ["ctx.ai.text.extract.emails"]   = new() { ["es"]="📧 Extraer emails",["en"]="📧 Extract emails",["pt"]="📧 Extrair e-mails",["fr"]="📧 Extraire les e-mails",["de"]="📧 E-Mails extrahieren",["zh"]="📧 提取邮箱",["ru"]="📧 Извлечь email",["ja"]="📧 メールを抽出" },
        ["ctx.ai.text.extract.phones"]   = new() { ["es"]="📞 Extraer teléfonos",["en"]="📞 Extract phone numbers",["pt"]="📞 Extrair telefones",["fr"]="📞 Extraire les téléphones",["de"]="📞 Telefonnummern extrahieren",["zh"]="📞 提取电话号码",["ru"]="📞 Извлечь телефоны",["ja"]="📞 電話番号を抽出" },
        ["ctx.ai.link.explain"]          = new() { ["es"]="🔍 Explicar a dónde lleva",["en"]="🔍 Explain where it leads",["pt"]="🔍 Explicar para onde leva",["fr"]="🔍 Expliquer où ça mène",["de"]="🔍 Erklären, wohin es führt",["zh"]="🔍 解释链接去向",["ru"]="🔍 Куда ведёт ссылка",["ja"]="🔍 リンク先を説明" },
        ["ctx.ai.link.preview"]          = new() { ["es"]="📄 Previsualizar contenido",["en"]="📄 Preview content",["pt"]="📄 Pré-visualizar conteúdo",["fr"]="📄 Aperçu du contenu",["de"]="📄 Inhalt vorschau",["zh"]="📄 预览内容",["ru"]="📄 Предпросмотр",["ja"]="📄 コンテンツをプレビュー" },
        ["ctx.ai.image.describe"]        = new() { ["es"]="👁 Describir imagen",["en"]="👁 Describe image",["pt"]="👁 Descrever imagem",["fr"]="👁 Décrire l'image",["de"]="👁 Bild beschreiben",["zh"]="👁 描述图像",["ru"]="👁 Описать изображение",["ja"]="👁 画像を説明" },
        ["ctx.ai.image.ocr"]             = new() { ["es"]="📖 Extraer texto (OCR)",["en"]="📖 Extract text (OCR)",["pt"]="📖 Extrair texto (OCR)",["fr"]="📖 Extraire le texte (OCR)",["de"]="📖 Text extrahieren (OCR)",["zh"]="📖 提取文字 (OCR)",["ru"]="📖 Извлечь текст (OCR)",["ja"]="📖 テキスト抽出 (OCR)" },
        ["ctx.ai.page.summarize"]        = new() { ["es"]="📝 Resumir página (TL;DR)",["en"]="📝 Summarise page (TL;DR)",["pt"]="📝 Resumir página (TL;DR)",["fr"]="📝 Résumer la page (TL;DR)",["de"]="📝 Seite zusammenfassen (TL;DR)",["zh"]="📝 总结页面 (TL;DR)",["ru"]="📝 Резюме страницы (TL;DR)",["ja"]="📝 ページを要約 (TL;DR)" },
        ["ctx.ai.page.bullets"]          = new() { ["es"]="🔑 Puntos clave",["en"]="🔑 Key points",["pt"]="🔑 Pontos-chave",["fr"]="🔑 Points clés",["de"]="🔑 Kernpunkte",["zh"]="🔑 关键要点",["ru"]="🔑 Ключевые пункты",["ja"]="🔑 重要ポイント" },
        ["ctx.ai.page.translate"]        = new() { ["es"]="🌐 Traducir página",["en"]="🌐 Translate page",["pt"]="🌐 Traduzir página",["fr"]="🌐 Traduire la page",["de"]="🌐 Seite übersetzen",["zh"]="🌐 翻译页面",["ru"]="🌐 Перевести страницу",["ja"]="🌐 ページを翻訳" },
        ["ctx.ai.page.eli5"]             = new() { ["es"]="🎯 Explicar nivel ELI5",["en"]="🎯 Explain ELI5",["pt"]="🎯 Explicar nível ELI5",["fr"]="🎯 Expliquer niveau ELI5",["de"]="🎯 ELI5 erklären",["zh"]="🎯 ELI5解释",["ru"]="🎯 Объяснить ELI5",["ja"]="🎯 ELI5で説明" },

        // ── Session restore (Phase 3 / Sprint 3) ─────────────────────────
        ["session.recover.title"]        = new() { ["es"]="VELO se cerró inesperadamente",["en"]="VELO closed unexpectedly",["pt"]="VELO fechou inesperadamente",["fr"]="VELO s'est fermé de façon inattendue",["de"]="VELO wurde unerwartet beendet",["zh"]="VELO 意外关闭",["ru"]="VELO неожиданно закрылся",["ja"]="VELOが予期せず終了しました" },
        ["session.recover.body"]         = new() { ["es"]="VELO se cerró inesperadamente la última vez. ¿Restaurar las {0} pestañas que estaban abiertas?",["en"]="VELO closed unexpectedly last time. Restore the {0} tabs that were open?",["pt"]="O VELO fechou inesperadamente da última vez. Restaurar as {0} abas que estavam abertas?",["fr"]="VELO s'est fermé de façon inattendue la dernière fois. Restaurer les {0} onglets ouverts ?",["de"]="VELO wurde beim letzten Mal unerwartet beendet. Sollen die {0} geöffneten Tabs wiederhergestellt werden?",["zh"]="VELO 上次意外关闭。是否恢复 {0} 个打开的标签页？",["ru"]="В прошлый раз VELO закрылся неожиданно. Восстановить {0} открытых вкладок?",["ja"]="前回VELOが予期せず終了しました。開いていた{0}個のタブを復元しますか？" },
        ["session.restore.title"]        = new() { ["es"]="Restaurar pestañas",["en"]="Restore tabs",["pt"]="Restaurar abas",["fr"]="Restaurer les onglets",["de"]="Tabs wiederherstellen",["zh"]="恢复标签页",["ru"]="Восстановить вкладки",["ja"]="タブを復元" },
        ["session.restore.body"]         = new() { ["es"]="Tenías {0} pestañas abiertas la última vez. ¿Restaurarlas?\n\n  Sí       → Restaurar siempre (sin volver a preguntar).\n  No       → No restaurar nunca.\n  Cancelar → Restaurar solo esta vez.",["en"]="You had {0} tabs open last time. Restore them?\n\n  Yes      → Always restore (don't ask again).\n  No       → Never restore.\n  Cancel   → Restore just this once.",["pt"]="Você tinha {0} abas abertas da última vez. Restaurá-las?\n\n  Sim      → Sempre restaurar (não perguntar mais).\n  Não      → Nunca restaurar.\n  Cancelar → Restaurar apenas desta vez.",["fr"]="Vous aviez {0} onglets ouverts la dernière fois. Les restaurer ?\n\n  Oui      → Toujours restaurer (ne plus demander).\n  Non      → Ne jamais restaurer.\n  Annuler  → Restaurer juste cette fois.",["de"]="Sie hatten beim letzten Mal {0} Tabs geöffnet. Wiederherstellen?\n\n  Ja        → Immer wiederherstellen (nicht erneut fragen).\n  Nein      → Nie wiederherstellen.\n  Abbrechen → Nur dieses Mal wiederherstellen.",["zh"]="上次有 {0} 个标签页打开。要恢复吗？\n\n  是   → 始终恢复（不再询问）。\n  否   → 从不恢复。\n  取消 → 仅本次恢复。",["ru"]="В прошлый раз было открыто {0} вкладок. Восстановить?\n\n  Да      → Всегда восстанавливать (не спрашивать снова).\n  Нет     → Никогда не восстанавливать.\n  Отмена  → Восстановить только в этот раз.",["ja"]="前回{0}個のタブが開いていました。復元しますか？\n\n  はい     → 常に復元（次回以降聞かない）。\n  いいえ   → 復元しない。\n  キャンセル → 今回だけ復元。" },
    };
}
