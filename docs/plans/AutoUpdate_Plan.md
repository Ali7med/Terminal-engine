# خطة: التحديث التلقائيّ من إصدارات GitHub

> **الحالة: مُنفَّذة** — النسخة 1.49.0 (2026-07-22). راجع قسم «ما نُفِّذ» في الأسفل.

## السياق (لماذا)

المطلوب: التطبيق يفحص GitHub تلقائياً ← يخبر المستخدم «توجد نسخة جديدة» ← المستخدم يضغط «تحديث»
← يُنزّل ويُركّب ويُشغّل النسخة الجديدة.

**البنية التحتية كانت موجودة بالكامل** — لم تكن الحاجة لبنائها من الصفر:

| الموجود مسبقاً | الملف |
|---|---|
| Velopack 1.2.0 + `RuntimeIdentifiers=win-x64` + self-contained عند النشر | `TerminalLauncher.csproj` |
| `VelopackApp.Build().Run()` كأوّل سطر في العمليّة (خطّافات التثبيت/التحديث) | `Program.cs` |
| `UpdateService` — فحص + تنزيل صامت من `GithubSource` | `Services/UpdateService.cs` |
| استدعاء الفحص عند `ApplicationIdle` بعد الإقلاع | `App.xaml.cs` |
| workflow يبني `vpk pack` وينشر إصداراً عند دفع وسم `v*`، مع حارس تطابق الوسم مع `AppVersion.Current` | `.github/workflows/release.yml` |

**الفجوتان الحقيقيتان:**

1. **لا وسم ولا إصدار منشور على GitHub** — مهما كان الكود سليماً فـ `CheckForUpdatesAsync` يعود `null`
   دائماً. هذا كان سبب عدم عمل التحديث.
2. **السياسة كانت صامتة وبلا موافقة** — يُنزَّل تلقائياً في الخلفيّة ويُطبَّق فقط عند الخروج
   (`WaitExitThenApplyUpdates(silent: true, restart: false)`): لا سؤال، لا تقدّم مرئيّ، لا إعادة تشغيل.

**النتيجة المستهدفة:** فحص صامت ← إشعار قابل للنقر + شارة دائمة على شريط العنوان ← حوار
«نسخة x.y.z متوفّرة» ← شريط تقدّم للتنزيل ← حوار «أعد التشغيل الآن / لاحقاً» ← إطلاق النسخة الجديدة.

---

## 1) نشر الإصدار الأساس

`release.yml` يتطلّب أن يطابق الوسمُ `AppVersion.Current` في الكوميت الموسوم:

```bash
git tag v1.48.2 <commit-of-1.48.2> && git push origin v1.48.2
```

## 2) إعادة تصميم `Services/UpdateService.cs` (جوهر العمل)

فصل «الفحص» عن «التنزيل» عن «التطبيق» بدل الدالّة الواحدة `CheckAndDownloadAsync`:

- `Task CheckAsync(bool silent)` — يفحص فقط ويخزّن `UpdateInfo`. عند `silent: false` (طلب يدويّ)
  يجيب في كلّ الحالات: تحديث متوفّر · أنت على أحدث نسخة · تعذّر الوصول. وعند `true` (الإقلاع) لا يتكلّم
  إلّا حين يوجد جديد.
- `event Action<string>? UpdateAvailable` — تشترك به `MainWindow` لإظهار الشارة.
- `Task DownloadAndApplyAsync(Window? owner)` — التدفّق الكامل بموافقة:
  1. `AppDialog.Confirm(...)` بزرّي «تحديث الآن» (لكنة) و«لاحقاً».
  2. `NotificationService.Progress(...)` ثمّ
     `manager.DownloadUpdatesAsync(info, pct => p.Report(pct / 100.0, $"{pct}%"), token)`
     — Velopack يمرّر `Action<int>` نسبة مئويّة، و`ProgressNotification.Report` يوجّه إلى خيط الواجهة
     داخلياً عبر `NotificationService.Dispatch`. زرّ X في البطاقة يُلغي عبر `CancellationTokenSource`.
  3. حوار ثانٍ: **الآن** ⇒ `ApplyUpdatesAndRestart(asset)` · **لاحقاً** ⇒ يبقى مسار
     `ArmApplyOnExit()`/`OnAppExit` كما هو (مهلة المحدِّث 60 ثانية تمنع نداءه فور التنزيل).
- القاعدتان الحاكمتان محفوظتان: **لا ترمي أبداً** (كلّ مسار داخل `try` + `CrashReporter.Log`، ويُعرَض
  الخطأ للمستخدم فقط حين يكون هو من طلب العمليّة) و**لا أثر في التطوير** (كلّ شيء يمرّ عبر
  `Manager.Value is null` أوّلاً).
- مانع التداخل `Interlocked` يغطّي الفحص والتنزيل معاً.

## 3) واجهة الإشعار والشارة

- **توست قابل للنقر:** `NotificationService.Primary` يقبل `Action? onClick` اختياريّاً — يُربط بـ
  `MouseLeftButtonUp` على البطاقة مع `Cursor=Hand`، مع استثناء زرّ الإغلاق عبر مقارنة `OriginalSource`.
- **الشارة:** `WhatsNewButton` ملفوف بـ `Grid` مع `Ellipse x:Name="UpdateDot"` بلون `Brush.Accent`،
  `IsHitTestVisible=False`، تظهر عند حدث `UpdateAvailable` وتبقى حتّى يُطبَّق التحديث.
- **مسار يدويّ:** `MenuItem` «تحقّق من التحديثات» داخل `ToolsMenu`، يظهر فقط حين `IsInstalled`.
  والنقر على `WhatsNewButton` وقت ظهور الشارة يفتح حوار التحديث بدل لوحة «ما الجديد».

## 4) الترجمة (`Services/Localization.cs`)

مفاتيح عربي/إنجليزي: `update.available` · `update.availableMsg` · `update.confirmTitle` ·
`update.confirmMsg` · `update.now` · `update.later` · `update.downloading` · `update.restartTitle` ·
`update.restartMsg` · `update.restartNow` · `update.ready` · `update.readyMsg` · `update.upToDate` ·
`update.upToDateMsg` · `update.failed` · `update.failedMsg` · `update.check` · `tip.update`.
تُطبَّق في `MainWindow.ApplyLanguage`. أرقام النسخ لاتينيّة دائماً عبر `CultureInfo.InvariantCulture`.

## 5) الدستور

رفع `AppVersion.Current` و`ReleasedDate`، ومدخلة مطابقة في `CHANGELOG.md`.

---

## ما نُفِّذ (1.49.0)

| ملف | التغيير |
|---|---|
| [`Services/UpdateService.cs`](../../Services/UpdateService.cs) | إعادة هيكلة كاملة (جوهر العمل) |
| [`Services/NotificationService.cs`](../../Services/NotificationService.cs) | `onClick` اختياريّ في `Primary` |
| [`Services/Localization.cs`](../../Services/Localization.cs) | 18 مفتاحاً جديداً |
| [`MainWindow.xaml`](../../MainWindow.xaml) | `UpdateDot` + بند «تحقّق من التحديثات» |
| [`MainWindow.xaml.cs`](../../MainWindow.xaml.cs) | اشتراك بالحدث، معالجات النقر، `ApplyLanguage` |
| [`App.xaml.cs`](../../App.xaml.cs) | النداء صار `CheckAsync(silent: true)` |
| [`Services/AppVersion.cs`](../../Services/AppVersion.cs) + [`CHANGELOG.md`](../../CHANGELOG.md) | 1.49.0 — 2026-07-22 |

الوسمان `v1.48.2` (الأساس) و`v1.49.0` مدفوعان — `release.yml` ينشر لكلٍّ منهما `Setup.exe` و`.nupkg`
وملفّ `RELEASES`.

## التحقّق

1. `dotnet build TerminalLauncher.csproj -c Release` ⇒ صفر تحذيرات وصفر أخطاء. ✔
2. **التطوير:** تشغيل عاديّ ⇒ لا شبكة، لا شارة، لا بند قائمة (`IsInstalled == false`).
3. **الحلقة الكاملة:**
   - ثبّت `Setup.exe` الخاصّ بـ **v1.48.2** من إصدار GitHub وشغّله.
   - يفحص عند الإقلاع ⇒ إشعار «تحديث متوفّر — 1.49.0» + نقطة على شريط العنوان.
   - النقر ⇒ حوار تحديث ⇒ شريط تقدّم ⇒ «أعد التشغيل الآن» ⇒ يُفتح على 1.49.0 مع لوحة «ما الجديد».
   - مسار «لاحقاً»: أغلق التطبيق ⇒ يُطبَّق بصمت ⇒ الفتح التالي على 1.49.0.
4. **فشل الشبكة:** اقطع الإنترنت ونفّذ الفحص اليدويّ ⇒ توست خطأ مهذّب، لا انهيار، سطر في `CrashReporter`.
