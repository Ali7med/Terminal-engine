# مراقب الخوادم (Server Monitor) — خطة التطوير

> أداة مستقلّة داخل **TerminalLauncher** لمراقبة **أداء** الخوادم و**تحليل تخزينها/ملفاتها** عبر SSH.
> مصدرها خطّة المستخدم `Storage_Analyzer_Feature_Plan.md` + طلب «مراقبة الأداء والملفات».

## الهدف

نافذة مستقلّة (`StorageAnalyzerWindow`/`ServerMonitorWindow`) تُفتح من زرّ في شريط عنوان
النافذة الرئيسة، تتيح:

1. **إدارة بروفايلات الخوادم** (Host/Port/User/Password أو Private Key) — محفوظة محلّياً بأسرار مُعمّاة (DPAPI).
2. **الاتصال عبر SSH** (تنفيذ أوامر) و**SFTP** (تصفّح/تحميل/حذف الملفات).
3. **تحليل المساحة**: `df` (الأقراص)، `du` (المجلّدات)، أكبر الملفّات، Treemap مرئيّ.
4. **مراقبة الأداء**: CPU/RAM/Load/Uptime، أعلى العمليّات، حيّاً (تحديث دوريّ).
5. **العمليّات على الملفّات**: تنزيل/حذف/إعادة تسمية + عارض سجلّات (Log tail) + بحث + تصدير.

---

## القرارات المعماريّة (مثبَّتة)

| القرار | الاختيار | السبب |
|---|---|---|
| سطح الدمج | **نافذة مستقلّة** | مساحة أوسع لِلَوحات متعدّدة (شجرة + Treemap + جدول + إحصاءات) |
| طبقة SSH | **SSH.NET (Renci)** | المكتبة القياسيّة في .NET لـ SSH exec + SFTP؛ تسرّع كلّ الميزات |
| المنطق النقيّ | مشروع محرّك جديد **`engine/Terminal.Servers`** (+ `.Tests`) | يطابق فلسفة الحلّ: محرّك نقيّ مُختبَر + UI فوقه. المحلّلات (df/du/ps) قابلة للاختبار بلا شبكة |
| التخزين | جدول **`server_profiles`** في `Terminal.Storage` (SQLite) | نفس نمط `SessionStore`/`SettingsSqliteStore` — كلّ ميزة تملك جدولها |
| الأسرار | تعمية **DPAPI** (`System.Security.Cryptography.ProtectedData`) مربوطة بحساب Windows | قرار الحلّ للأسرار؛ لا كلمات مرور خام على القرص |
| الإصدار | يتبع الدستور: رفع `AppVersion` + مدخلة `CHANGELOG.md` + «ما الجديد» كلّ موجة | إلزاميّ |
| التعريب | كلّ نصّ عبر `Loc.T(...)` (ar/en) + `NumberSubstitution=European` | اصطلاحات الواجهة |

**تنبيه تداخل:** هذا يتقاطع مع مهام Phase 3 القائمة (T-301 SSH / T-303 SFTP / T-309 System Monitor
/ T-310 Logs). تُعتبر هذه الأداة **التنفيذ الموحّد** لتلك المهام؛ عند إكمالها تُعلَّم تلك المهام مكتملةً عبرها.

---

## طبقات المشروع

```
engine/Terminal.Servers/            (net10.0، نقيّ، بلا UI)
  Models/    ServerProfile · DiskInfo · DirEntry · FileEntry · PerfSnapshot · ProcessInfo
  Ssh/       ISshConnection · SshNetConnection (SSH.NET) · ISftpBrowser
  Scan/      StorageScanner (df/du/ls) · PerfMonitor (top/free/uptime/ps)
  Parsing/   DfParser · DuParser · LsParser · PerfParser   ← مُختبَرة بعيّنات مخرجات حقيقيّة
engine/Terminal.Servers.Tests/      (xunit — المحلّلات + السرّ)

Terminal.Storage/ServerProfileStore.cs   ← CRUD + بلوب سرّ مُعمّى
Services/SecretProtector.cs               ← DPAPI (WPF layer)
Views/ServerMonitorWindow.xaml(.cs)       ← النافذة + اللوحات
Controls/ (حسب الحاجة) TreemapControl …
```

---

## المراحل والموجات

### الموجة 1 — الأساس (Foundation) ✅ **مكتملة** (v1.2.0)
- [x] مشروع `Terminal.Servers` + النماذج + `ISshConnection`/`SshNetConnection` (SSH.NET) + محلّلات df/du/find/free/ps.
- [x] `Terminal.Servers.Tests` (9 اختبارات محلّلات + shell-quote) — تمرّ.
- [x] `ServerProfileStore` (SQLite، 5 اختبارات) + `SecretProtector` (DPAPI) + `ServerProfileService`.
- [x] `ServerMonitorWindow` قشرة + لوحة **بروفايلات الخوادم** (إضافة/تعديل/حذف/نسخ/بحث + محرّر بألوان + Test Connection).
- [x] الاتصال/قطع + مؤشّر الحالة (خمول/جارٍ/متّصل/فشل) + ختم «آخر اتصال» + فحص الأقراص السريع (df) بأشرطة امتلاء.
- [x] زرّ `ServerMonitorButton` في شريط عنوان `MainWindow` يفتح النافذة + مفاتيح تعريب (ar/en) + الإصدار 1.2.0 + CHANGELOG + ذاكرة.
- **البناء:** 0 تحذير / 0 خطأ. **الاختبارات:** 9 (servers) + 52 (storage) تمرّ.
- **المتبقّي للموجة 2:** إعادة الاتصال التلقائيّ، الفرز اليدويّ للقائمة، أيقونات موحّدة بدل glyph inline في المحرّر.

### الموجة 2 — تحليل المساحة ✅ **مكتملة** (v1.3.0)
- [x] Quick Scan (`df`) — منجَز في الموجة 1 (تبويب «الأقراص»).
- [x] Custom Folder Scan (`du --max-depth=1`) عبر `ScanSubfoldersAsync` (إجماليّ + أبناء مباشرون).
- [x] مستكشف المجلّدات (TreeView بتحميل كسول: الاسم/المسار/الحجم + توسيع عند الطلب) + Copy Path (زرّ + قائمة سياقيّة).
- [x] Favorites (`/ /var /var/log /var/www /home /etc /opt /tmp`) بنقرة واحدة + شريط مسار مخصّص (Enter/فحص).
- **البناء:** 0/0. **الاختبارات:** 12 (servers) — منها 3 جديدة لـ `StorageScanner`.
- **مؤجَّل للموجة 3:** عدد الملفّات/المجلّدات لكلّ عقدة (يحتاج `find|wc`)؛ Refresh لعقدة مفردة.

### الموجة 3 — الملفّات والعمليّات ✅ **مكتملة** (v1.4.0)
- [x] أكبر الملفّات (DataGrid: Name/Ext/Size/Modified/Path + فرز أعمدة + بحث فوريّ + عدّاد).
- [x] العمليّات: Download (SFTP) / Delete (بتأكيد) / Rename / Copy Path عبر قائمة سياقيّة.
- [x] Log Viewer (آخر 1000 سطر عبر `tail` + بحث بـ Enter مع التفاف).
- [x] التصدير: CSV (Excel لاحقاً في الموجة 5).
- **المحرّك:** `SshNetSftp` (تنزيل) + `FileOperations` (rm/mv/tail، مقتبسة بأمان) + `ConnectionInfoFactory`. **الاختبارات:** 18 (servers).
- **البناء:** يترجم بلا أخطاء؛ يبقى قفل نسخ عند تشغيل الأداة (أغلقها لبناء نظيف).
- **مؤجَّل:** فرز/بحث الشجرة، عدد الملفّات لكلّ مجلّد، Excel.

### الموجة 4 — المراقبة المرئيّة والأداء ✅ **معظمها مكتمل** (v1.9.0 — لوحة القيادة)
- [x] Treemap لاستهلاك المساحة (أشرطة نسبيّة ملوّنة للمجلّدات الجذر) — في لوحة القيادة.
- [x] لوحة الأداء: CPU Load/RAM/Uptime + أعلى العمليّات + نظرة عامّة (نظام/نواة/معالج/IP) عبر `SystemInfoScanner`+`PerfMonitor`.
- [x] لوحة القيادة الافتراضيّة عند الاتّصال + شجرة + أكبر الملفّات.
- [ ] **مؤجَّل:** التحديث الدوريّ التلقائيّ (حاليّاً يدويّ عبر «تحديث»)؛ Treemap تفاعليّ (نقر مربّع)؛ إحصاءات Average File Size.

### الموجة 5 — المتقدّم (جارٍ)
- [x] **مراقبة حيّة + تنبيهات** (v1.17.0): تحديث دوريّ للأداء + تنبيه قرص≥90%/حِمل عالٍ.
- [x] **Multi-Select + حذف/تنزيل جماعيّ + رفع (Upload) SFTP + mkdir** (v1.18.0).
- [x] **إدارة الخادم** (v1.19.0): عمليّات (kill) + خدمات systemd (start/stop/restart) + منافذ مُنصِتة.
- [x] Lazy Loading (أكبر الملفّات بزرّ) + تسريع (نداء واحد + du -x + توازٍ) (v1.15–1.16).
- [ ] **المتبقّي:** Compress/Extract · Pie/Bar Charts · Compare Scans · Safe Delete+Restore · Scheduler · PDF Reports.
- **التالي المطلوب:** صقل + تحقّق على خادم حقيقيّ (الوجهة 4).

---

## معايير القبول لكلّ موجة
- بناءٌ بلا تحذير (`dotnet build`), محلّلات المحرّك مغطّاة باختبارات xunit تمرّ.
- كلّ نصّ مترجَم (ar/en)، أرقام لاتينية، ثيم `DynamicResource Brush.*`، اتّجاه يتبع اللغة.
- الأسرار لا تُكتب خاماً؛ الحذف يتطلّب تأكيداً؛ عمليّات SSH على خيط خلفيّ (لا تجميد UI).
- رفع الإصدار + مدخلة CHANGELOG مطابقة لكلّ موجة مكتملة.
