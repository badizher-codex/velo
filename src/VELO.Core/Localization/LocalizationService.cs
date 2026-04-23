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
    };
}
