using System;
using System.Collections.Generic;
using System.Windows;

namespace TerminalLauncher.Services;

/// <summary>لغة الواجهة.</summary>
public enum AppLang { Ar, En }

/// <summary>
/// تعريب بسيط وقت التشغيل (عربي/إنجليزي) + اتجاه الواجهة (RTL/LTR).
/// القيم مُضمّنة كأزواج (عربي، إنجليزي)؛ الطبقات تستدعي <see cref="T"/> بمفتاح.
/// </summary>
public static class Loc
{
    public static AppLang Current { get; private set; } = AppLang.Ar;

    /// <summary>يُطلَق عند تغيّر اللغة (تُعيد الطبقات تطبيق النصوص/الاتجاه).</summary>
    public static event Action? Changed;

    /// <summary>اتجاه الواجهة الموافق للّغة الحاليّة.</summary>
    public static FlowDirection Flow => Current == AppLang.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>رمز اللغة للحفظ ("ar"/"en").</summary>
    public static string Code => Current == AppLang.En ? "en" : "ar";

    /// <summary>يضبط اللغة من رمز محفوظ دون إطلاق الحدث (للتهيئة).</summary>
    public static void InitFromCode(string? code)
        => Current = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? AppLang.En : AppLang.Ar;

    /// <summary>يبدّل اللغة ويُطلِق <see cref="Changed"/>.</summary>
    public static void Set(AppLang lang)
    {
        if (Current == lang) return;
        Current = lang;
        Changed?.Invoke();
    }

    /// <summary>النصّ الموافق للّغة الحاليّة (يعيد المفتاح نفسه إن لم يُعرَّف).</summary>
    public static string T(string key)
        => Table.TryGetValue(key, out var pair) ? (Current == AppLang.En ? pair.En : pair.Ar) : key;

    private static readonly Dictionary<string, (string Ar, string En)> Table = new()
    {
        ["app.title"]          = ("مشغّل الأوامر", "Command Launcher"),
        ["sidebar.saved"]      = ("الأوامر المحفوظة", "Saved commands"),
        ["sidebar.search"]     = ("بحث…", "Search…"),
        ["btn.run"]            = ("▶  تشغيل", "▶  Run"),
        ["hint.pick"]          = ("اختر أمراً واضغط «تشغيل» لفتح تيرمنال", "Pick a command and press Run to open a terminal"),
        ["hint.empty"]         = ("أو Ctrl+Shift+T لتيرمنال فارغ", "or Ctrl+Shift+T for an empty terminal"),
        ["settings.title"]     = ("الإعدادات", "Settings"),
        ["settings.appearance"]= ("المظهر", "Appearance"),
        ["settings.theme"]     = ("الثيم", "Theme"),
        ["settings.themehint"] = ("اختر ثيماً — يُطبَّق فوراً", "Pick a theme — applied instantly"),
        ["settings.showAllThemes"] = ("عرض كل الثيمات", "Show all themes"),
        ["settings.allThemes"] = ("كل الثيمات", "All themes"),
        ["tip.toggleSidebar"]  = ("إظهار/إخفاء الأوامر المحفوظة", "Show/hide saved commands"),
        ["settings.syncos"]    = ("مزامنة الفاتح/الداكن مع النظام", "Sync light/dark with OS"),
        ["settings.dark"]      = ("داكن", "Dark"),
        ["settings.light"]     = ("فاتح", "Light"),
        ["settings.accent"]    = ("لون التمييز", "Accent color"),
        ["settings.fontsize"]  = ("حجم الخطّ", "Font size"),
        ["settings.fontfamily"]= ("نوع الخطّ", "Font family"),
        ["settings.textcolor"] = ("لون الكتابة", "Text color"),
        ["settings.language"]  = ("اللغة", "Language"),
        ["settings.background"] = ("الخلفية", "Background"),
        ["settings.font"]       = ("الخطّ", "Font"),
        ["settings.appearanceBg"] = ("المظهر والخلفية", "Appearance & Background"),
        ["settings.langFont"]     = ("اللغة والخطّ", "Language & Font"),
        ["settings.autosave"]  = ("تُحفظ التفضيلات تلقائياً", "Preferences are saved automatically"),

        // ===== الخلفيّة (قوالب + صورة + شفافيّة التيرمنال) =====
        ["bg.section"]         = ("الخلفية", "Background"),
        ["bg.templates"]       = ("قوالب", "Templates"),
        ["bg.solids"]          = ("ألوان مصمتة", "Solid colors"),
        ["bg.gradients"]       = ("تدرّجات", "Gradients"),
        ["bg.patterns"]        = ("نقوش", "Patterns"),
        ["bg.themeDefault"]    = ("خلفية الثيم", "Theme default"),
        ["bg.custom"]          = ("صورة مخصّصة", "Custom image"),
        ["bg.choose"]          = ("اختر صورة", "Choose image"),
        ["bg.clear"]           = ("إزالة", "Remove"),
        ["bg.opacity"]         = ("شفافية التيرمنال", "Terminal opacity"),
        ["tip.newtab"]         = ("تيرمنال فارغ جديد (Ctrl+Shift+T)", "New empty terminal (Ctrl+Shift+T)"),
        ["tip.history"]        = ("سجلّ الأوامر", "Command history"),
        ["tip.settings"]       = ("الإعدادات", "Settings"),
        ["tip.add"]            = ("إضافة أمر", "Add command"),
        ["profiles.manage"]    = ("إدارة البروفايلات…", "Manage profiles…"),

        // ===== تأكيد اللصق (حماية) =====
        ["paste.title"]        = ("تأكيد اللصق", "Confirm paste"),
        ["paste.warnMulti"]    = ("أنت على وشك لصق نصّ متعدّد الأسطر في التيرمنال. راجعه قبل التنفيذ:",
                                  "You are about to paste multi-line text into the terminal. Review it before running:"),
        ["paste.warnDanger"]   = ("⚠ يحتوي النصّ الملصوق على أوامر قد تكون خطرة. راجعه بعناية قبل اللصق:",
                                  "⚠ The pasted text contains commands that may be dangerous. Review it carefully before pasting:"),
        ["paste.confirm"]      = ("لصق", "Paste"),
        ["paste.cancel"]       = ("إلغاء", "Cancel"),
        ["paste.lines"]        = ("سطر", "lines"),

        // ===== لوحة «ما الجديد» (What's New) =====
        ["whatsnew.title"]     = ("ما الجديد", "What's New"),
        ["whatsnew.empty"]     = ("لا توجد مستجدّات لعرضها.", "No changes to show yet."),
        ["whatsnew.highlights"]= ("أبرز المميّزات", "Highlights"),
        ["whatsnew.section.new"]      = ("جديد", "New"),
        ["whatsnew.section.improved"] = ("تحسينات", "Improved"),
        ["whatsnew.section.fixed"]    = ("إصلاحات", "Fixed"),
        ["tip.whatsnew"]       = ("ما الجديد / حول البرنامج", "What's New / About"),
        ["common.close"]       = ("إغلاق", "Close"),

        // ===== فتح المسار + محرّر الملفّ داخل التاب (T-7) =====
        ["menu.openFolder"]    = ("فتح المجلّد", "Open folder"),
        ["menu.openFile"]      = ("فتح الملفّ", "Open file"),
        ["editor.save"]        = ("حفظ", "Save"),
        ["editor.dontSave"]    = ("عدم الحفظ", "Don't save"),
        ["editor.cancel"]      = ("إلغاء", "Cancel"),
        ["editor.unsavedTitle"]= ("تغييرات غير محفوظة", "Unsaved changes"),
        ["editor.unsavedMessage"]=("توجد تغييرات غير محفوظة في هذا الملفّ. هل تريد حفظها قبل الإغلاق؟",
                                   "This file has unsaved changes. Do you want to save them before closing?"),

        // ===== مراقب الخوادم (Server Monitor) =====
        ["srv.title"]          = ("مراقب الخوادم", "Server Monitor"),
        ["tip.serverMonitor"]  = ("مراقب الخوادم (تخزين وأداء عبر SSH)", "Server Monitor (storage & performance over SSH)"),
        ["srv.servers"]        = ("الخوادم", "Servers"),
        ["srv.search"]         = ("بحث…", "Search…"),
        ["srv.add"]            = ("إضافة خادم", "Add server"),
        ["srv.edit"]           = ("تعديل", "Edit"),
        ["srv.delete"]         = ("حذف", "Delete"),
        ["srv.duplicate"]      = ("نسخ", "Duplicate"),
        ["srv.connect"]        = ("اتّصال", "Connect"),
        ["srv.disconnect"]     = ("قطع الاتّصال", "Disconnect"),
        ["srv.reconnect"]      = ("إعادة الاتّصال", "Reconnect"),
        ["srv.test"]           = ("اختبار الاتّصال", "Test connection"),
        ["srv.refresh"]        = ("تحديث", "Refresh"),
        ["srv.noServers"]      = ("لا خوادم بعد — أضِف خادماً للبدء", "No servers yet — add one to begin"),
        ["srv.pickServer"]     = ("اختر خادماً واضغط «اتّصال»", "Pick a server and press Connect"),
        ["srv.status.idle"]    = ("غير متّصل", "Disconnected"),
        ["srv.status.connecting"] = ("جارٍ الاتّصال…", "Connecting…"),
        ["srv.status.connected"]  = ("متّصل", "Connected"),
        ["srv.status.failed"]  = ("فشل الاتّصال", "Connection failed"),
        ["srv.status.reconnecting"] = ("انقطع الاتّصال — إعادة الاتّصال…", "Connection lost — reconnecting…"),
        ["srv.lastConnected"]  = ("آخر اتّصال", "Last connected"),
        ["srv.never"]          = ("أبداً", "Never"),
        ["srv.disks"]          = ("الأقراص والمساحة", "Disks & storage"),
        ["srv.col.mount"]      = ("نقطة التركيب", "Mount"),
        ["srv.col.size"]       = ("الحجم", "Size"),
        ["srv.col.used"]       = ("المستخدم", "Used"),
        ["srv.col.avail"]      = ("الفارغ", "Free"),
        ["srv.col.usePct"]     = ("النسبة", "Use%"),
        ["srv.testOk"]         = ("نجح الاتّصال بالخادم.", "Connection succeeded."),
        ["srv.testFail"]       = ("تعذّر الاتّصال بالخادم:", "Could not connect:"),

        // محرّر بروفايل الخادم
        ["srv.ed.addTitle"]    = ("إضافة خادم", "Add server"),
        ["srv.ed.editTitle"]   = ("تعديل الخادم", "Edit server"),
        ["srv.ed.name"]        = ("الاسم", "Name"),
        ["srv.ed.host"]        = ("المضيف (Host/IP)", "Host / IP"),
        ["srv.ed.port"]        = ("المنفذ", "Port"),
        ["srv.ed.user"]        = ("اسم المستخدم", "Username"),
        ["srv.ed.auth"]        = ("المصادقة", "Authentication"),
        ["srv.ed.authPassword"]= ("كلمة مرور", "Password"),
        ["srv.ed.authKey"]     = ("مفتاح خاصّ", "Private key"),
        ["srv.ed.password"]    = ("كلمة المرور", "Password"),
        ["srv.ed.key"]         = ("المفتاح الخاصّ (PEM)", "Private key (PEM)"),
        ["srv.ed.keyPass"]     = ("عبارة مرور المفتاح", "Key passphrase"),
        ["srv.ed.secretKept"]  = ("(محفوظ — اتركه فارغاً للإبقاء عليه)", "(saved — leave blank to keep)"),
        ["srv.ed.color"]       = ("لون مميّز", "Accent color"),
        ["srv.ed.notes"]       = ("ملاحظات", "Notes"),
        ["srv.ed.save"]        = ("حفظ", "Save"),
        ["srv.ed.cancel"]      = ("إلغاء", "Cancel"),
        ["srv.ed.deleteConfirm"] = ("حذف هذا الخادم نهائيّاً؟", "Delete this server permanently?"),

        // مستكشف المجلّدات (الموجة 2)
        ["srv.tab.dashboard"]  = ("لوحة القيادة", "Dashboard"),
        ["srv.tab.disks"]      = ("الأقراص", "Disks"),
        ["srv.tab.folders"]    = ("المجلّدات", "Folders"),

        // لوحة القيادة
        ["srv.dash.overview"]  = ("نظرة عامّة", "Overview"),
        ["srv.dash.topProc"]   = ("أعلى العمليّات", "Top processes"),
        ["srv.dash.treemap"]   = ("استهلاك المجلّدات (Treemap)", "Folders Usage (Treemap)"),
        ["srv.dash.largest"]   = ("أكبر الملفّات", "Largest Files"),
        ["srv.dash.loadLargest"] = ("عرض أكبر الملفّات", "Load largest files"),
        ["srv.dash.tree"]      = ("الشجرة", "Tree"),
        ["srv.dash.host"]      = ("المضيف", "Host"),
        ["srv.dash.os"]        = ("النظام", "OS"),
        ["srv.dash.kernel"]    = ("النواة", "Kernel"),
        ["srv.dash.cpu"]       = ("المعالج", "CPU"),
        ["srv.dash.ip"]        = ("العنوان", "IP"),
        ["srv.dash.cores"]     = ("نواة", "cores"),
        ["srv.dash.uptime"]    = ("التشغيل", "Uptime"),
        ["srv.dash.load"]      = ("الحِمل (1د)", "Load (1m)"),
        ["srv.dash.ram"]       = ("الذاكرة", "Memory"),
        ["srv.dash.rootDisk"]  = ("القرص /", "Disk /"),
        ["srv.live.toggle"]    = ("● مباشر", "● Live"),
        ["srv.live.updated"]   = ("آخر تحديث:", "Updated:"),
        ["srv.alert.diskFull"] = ("امتلاء قرص", "Disk almost full"),
        ["srv.alert.highLoad"] = ("حِمل عالٍ", "High load"),
        ["srv.folder.scan"]    = ("فحص", "Scan"),
        ["srv.folder.copyPath"]= ("نسخ المسار", "Copy path"),
        ["srv.folder.favorites"] = ("المفضّلة", "Favorites"),
        ["srv.folder.total"]   = ("الإجماليّ", "Total"),
        ["srv.folder.subfolders"] = ("مجلّد فرعيّ", "subfolders"),
        ["srv.folder.empty"]   = ("لا مجلّدات فرعيّة", "No subfolders"),
        ["srv.folder.loading"] = ("جارٍ الفحص…", "Scanning…"),
        ["srv.folder.pathHint"]= ("مسار مطلق (مثل /var)", "Absolute path (e.g. /var)"),
        ["srv.tree.openFiles"] = ("عرض الملفّات هنا", "Show files here"),
        ["srv.tree.openFolders"] = ("فتح في المجلّدات", "Open in Folders"),
        ["srv.tree.refresh"]   = ("تحديث المجلّد", "Refresh folder"),
        ["srv.tree.deleteFolder"] = ("حذف المجلّد", "Delete folder"),
        ["srv.tree.deleteFolderConfirm"] = ("حذف هذا المجلّد وكلّ محتوياته نهائيّاً؟ لا يمكن التراجع.",
                                            "Delete this folder and all its contents permanently? This cannot be undone."),
        ["srv.tree.cantDeleteRoot"] = ("لا يمكن حذف الجذر.", "Cannot delete the root folder."),
        ["srv.tree.upload"]    = ("رفع ملفّات هنا", "Upload files here"),
        ["srv.tree.newFolder"] = ("مجلّد جديد", "New folder"),
        ["srv.tree.folderCreated"] = ("تمّ إنشاء المجلّد", "Folder created"),
        ["srv.up.title"]       = ("رفع", "Upload"),
        ["srv.up.uploading"]   = ("جارٍ الرفع", "Uploading"),
        ["srv.up.cancelled"]   = ("أُلغي الرفع", "Upload cancelled"),
        ["srv.file.deleteManyConfirm"] = ("حذف الملفّات المحدّدة نهائيّاً؟", "Delete the selected files permanently?"),

        // الملفّات والعمليّات (الموجة 3)
        ["srv.tab.files"]      = ("الملفّات", "Files"),
        ["srv.tab.mgmt"]       = ("الإدارة", "Manage"),

        // الإدارة (عمليّات + خدمات + منافذ)
        ["srv.mgmt.processes"] = ("العمليّات", "Processes"),
        ["srv.mgmt.services"]  = ("الخدمات", "Services"),
        ["srv.mgmt.ports"]     = ("المنافذ المُنصِتة", "Listening ports"),
        ["srv.mgmt.kill"]      = ("إنهاء العمليّة", "Kill process"),
        ["srv.mgmt.kill9"]     = ("إنهاء إجباريّ (-9)", "Force kill (-9)"),
        ["srv.mgmt.killConfirm"] = ("إنهاء العمليّة رقم", "Kill process"),
        ["srv.mgmt.killed"]    = ("تمّ إنهاء العمليّة", "Process killed"),
        ["srv.mgmt.start"]     = ("تشغيل", "Start"),
        ["srv.mgmt.stop"]      = ("إيقاف", "Stop"),
        ["srv.mgmt.restart"]   = ("إعادة تشغيل", "Restart"),
        ["srv.files.scan"]     = ("أكبر الملفّات", "Largest files"),
        ["srv.files.search"]   = ("بحث في النتائج…", "Search results…"),
        ["srv.files.filesWord"]= ("ملفّ", "files"),
        ["srv.files.export"]   = ("تصدير CSV", "Export CSV"),
        ["srv.col.name"]       = ("الاسم", "Name"),
        ["srv.col.ext"]        = ("الامتداد", "Extension"),
        ["srv.col.modified"]   = ("التعديل", "Modified"),
        ["srv.col.path"]       = ("المسار", "Path"),
        ["srv.file.download"]  = ("تنزيل", "Download"),
        ["srv.file.viewLog"]   = ("عرض (آخر 1000 سطر)", "View (last 1000 lines)"),
        ["srv.file.rename"]    = ("إعادة تسمية", "Rename"),
        ["srv.file.copyPath"]  = ("نسخ المسار", "Copy path"),
        ["srv.file.whichContainer"] = ("اعرف الحاوية", "Which container?"),
        ["srv.docker.resolving"] = ("جارٍ تحديد الحاوية…", "Identifying container…"),
        ["srv.docker.notDocker"] = ("هذا الملفّ ليس داخل طبقة overlay2 ولا حجم Docker.", "This file is not inside a Docker overlay2 layer or volume."),
        ["srv.docker.overlayLayer"] = ("طبقة overlay2", "overlay2 layer"),
        ["srv.docker.volume"]  = ("حجم Docker", "Docker volume"),
        ["srv.docker.noOwner"] = ("لم يُعثر على حاوية تملك طبقة الكتابة هذه (قد تكون طبقة صورة أساس مشتركة، أو Docker غير متوفّر/بلا صلاحية).", "No container owns this writable layer (it may be a shared base-image layer, or Docker is unavailable)."),
        ["srv.docker.noVolOwner"] = ("لا حاوية تُركّب هذا الحجم حاليّاً.", "No container currently mounts this volume."),
        ["srv.docker.image"]   = ("الصورة", "Image"),
        ["srv.docker.status"]  = ("الحالة", "Status"),
        ["srv.docker.rwLayer"] = ("الملفّ في طبقة الكتابة لهذه الحاوية (المالك الحصريّ).", "File is in this container's writable layer (exclusive owner)."),
        ["srv.docker.mounted"] = ("حجم مُركَّب في هذه الحاوية.", "Volume mounted in this container."),
        ["srv.docker.ownerTitle"] = ("الحاوية المالكة", "Owning container"),
        ["srv.docker.copyName"] = ("نسخ الاسم", "Copy name"),
        ["srv.docker.copyId"]  = ("نسخ المعرّف", "Copy ID"),
        ["srv.file.delete"]    = ("حذف", "Delete"),
        ["srv.file.deleteTitle"] = ("حذف الملفّ", "Delete file"),
        ["srv.file.deleteConfirm"] = ("حذف هذا الملفّ نهائيّاً؟", "Delete this file permanently?"),
        ["srv.file.downloadOk"]= ("تمّ التنزيل بنجاح.", "Download complete."),
        ["srv.file.downloadFail"] = ("تعذّر التنزيل:", "Download failed:"),
        ["srv.file.opFail"]    = ("فشلت العمليّة:", "Operation failed:"),
        ["srv.file.loading"]   = ("جارٍ الفحص…", "Scanning…"),
        ["srv.log.search"]     = ("بحث في السجلّ (Enter)…", "Search log (Enter)…"),
        ["srv.toast.copied"]   = ("تمّ نسخ المسار", "Path copied"),
        ["srv.folder.filesIn"] = ("ملفّات:", "Files in:"),
        ["srv.folder.noFiles"] = ("لا ملفّات مباشرة في هذا المجلّد", "No files directly in this folder"),
        ["srv.folder.detailHint"] = ("اضغط مجلّداً لعرض ملفّاته", "Click a folder to view its files"),
        ["srv.font.smaller"]   = ("تصغير خطّ التفاصيل", "Smaller detail font"),
        ["srv.font.larger"]    = ("تكبير خطّ التفاصيل", "Larger detail font"),
        ["srv.font.tip"]       = ("حجم خطّ التفاصيل", "Detail font size"),
        ["srv.dl.downloading"] = ("تنزيل", "Downloading"),
        ["srv.dl.cancelled"]   = ("أُلغي التنزيل", "Download cancelled"),
        ["srv.file.deleted"]   = ("تمّ الحذف", "Deleted"),
        ["srv.file.renamed"]   = ("تمّت إعادة التسمية", "Renamed"),
        ["srv.files.empty"]    = ("لا ملفّات", "No files"),
        ["srv.files.noMatch"]  = ("لا نتائج للبحث", "No matches"),

        // قائمة أدوات النظام (زرّ الأدوات في الواجهة الرئيسة)
        ["tools.menu"]         = ("أدوات النظام", "System tools"),
        ["tools.serverMonitor"]= ("مراقب الخوادم", "Server Monitor"),
    };
}
