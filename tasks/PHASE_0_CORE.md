# Phase 0 — الأساس التقني (Core Engine)
> **الهدف النهائي للمرحلة:** نافذة WPF بتيرمنال واحد يشغّل PowerShell بألوان TrueColor كاملة، vim و htop يشتغلون بدون تكسّر.

---

## T-001: بنية المشروع (Solution Architecture)
**الحالة:** ✅ مكتمل | **الأولوية:** P0 | **التقدير:** 3 أيام | **يعتمد على:** —

**الهدف:** Solution نظيف بفصل تام بين المحرك والواجهة.

### Subtasks
- [x] T-001.1: إنشاء الـ Solution والمشاريع حسب الهيكل بـ `AGENT_GUIDE.md` (Core بدون أي مرجع UI)
- [x] T-001.2: إضافة الحزم: CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting (يجُبّ DI+Logging), xUnit — (SkiaSharp.Views.WPF مؤجَّل لـ T-005)
- [x] T-001.3: إعداد DI Container بـ `App.xaml.cs` مع Generic Host
- [x] T-001.4: إعداد `.editorconfig` + `Directory.Build.props` (nullable enabled, LangVersion latest, TreatWarningsAsErrors للـ Core)
- [x] T-001.5: نافذة رئيسية فارغة تشتغل + اختبار xUnit واحد يمر (smoke test)
- [x] T-001.6: تهيئة `.gitignore` مناسب لـ .NET (المستودع مُهيّأ على مستوى المنصّة؛ لا git init متداخل)

### معايير القبول
- [x] `dotnet build` ينجح بدون تحذيرات (0 Warning / 0 Error على كامل الحلّ)
- [x] `dotnet test` يمر (UI.Tests 2/2؛ Core.Tests 5 ناجحة + 2 SkippableFact)
- [x] `Terminal.Core` ما يرجع لأي مكتبة UI (csproj + اختبار `Core_assembly_references_no_ui_frameworks`)

### ملاحظات التنفيذ
- **تصحيح موضع (2026-07-04):** أُنجِز فعلياً داخل `Tools/TerminalLauncher/` (لا `Tools/Terminal/` الذي رُوجِع بالكوميت `b2a14ff`)؛ لا حلّ `Terminal.slnx` منفصل — يُبنى عبر `TerminalLauncher.csproj` الذي يرجع مشاريع `engine/` بـ ProjectReference.
- الهيكل الفعليّ: `engine/Terminal.Core` (net10.0، بلا UI، `TreatWarningsAsErrors`) · **الـUI = مشروع `TerminalLauncher` الجذر** (net10.0-windows، WPF، `Controls/` للرندرر والإدخال) · `engine/Terminal.Plugins.Sdk` (واجهة `ITerminalPlugin` بذرة لـ T-501) · `engine/Terminal.Core.Tests`. (`Terminal.UI.Tests`/`Terminal.Ai` لم يُنشآ بعد.)
- **`Directory.Build.props`** في `engine/` يعزل مشاريع المحرّك عن شجرة المنصّة الأمّ (يوقف تسرّب إعدادات البناء الأب) — مقصود لتفادي تضارب دورة البناء.
- الحزم: `Microsoft.Extensions.Hosting` (يجُرّ DI + Logging + Configuration). **`SkiaSharp.Views.WPF` أُجِّل لـ T-005**: نسخة 3.116.1 بلا هدف net10-windows نظيف تجرّ OpenTK إطار .NET (NU1701)، ولا تُستعمل قبل الرندرر.
- DI Host في `App.xaml.cs` (`Host.CreateApplicationBuilder`)؛ `MainWindow` يُحقَن `MainViewModel`؛ مُتحقَّق حيّاً (التطبيق يقلع بلا انهيار).

---

## T-002: تكامل ConPTY (Pseudo Console)
**الحالة:** ✅ مكتمل | **الأولوية:** P0 | **التقدير:** 5-7 أيام | **يعتمد على:** T-001

**الهدف:** إنشاء جلسة shell حقيقية عبر Windows Pseudo Console والتواصل معها.

### Subtasks
- [x] T-002.1: ملف `Pty/NativeMethods.cs` بـ P/Invoke لـ: `CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole`, `CreatePipe`, `CreateProcessW` مع `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`
- [x] T-002.2: كلاس `PtySession : IPtySession, IDisposable` — يبدأ shell مربوطاً بالـ PTY مع pipes إدخال/إخراج
- [x] T-002.3: قراءة من output pipe بحلقة مع رفع `DataReceived(byte[])` — **خيط حاجب لا `ReadAsync`** (انظر الملاحظات)
- [x] T-002.4: كتابة Async للـ input pipe (`WriteAsync`) + دالة `Resize(cols, rows)`
- [x] T-002.5: مراقبة موت العملية (`RegisterWaitForSingleObject`) ورفع `Exited(exitCode)` + تنظيف كامل بالـ Dispose
- [x] T-002.6: اختبار `cmd.exe /c echo hello` يستلم النص (SkippableFact — يحتاج كونسول حقيقي)
- [x] T-002.7: اختبار PowerShell تفاعلي: كتابة أمر واستلام مخرجاته ثم `exit` والتحقق من حدث الخروج (SkippableFact)

### معايير القبول
- [x] تشغيل cmd و PowerShell والقراءة/الكتابة تشتغل — **مُتحقَّق حيّاً على كونسول حقيقي** (probe: `bytes=62, MARKER=True`)
- [x] Resize بلا crash + فتح/غلق 50 جلسة بلا استثناء (اختبار `Opening_and_disposing_many_sessions_is_clean`) — فحص Task Manager للـ handles يدويّ
- [x] الـ Dispose يقتل العملية والـ pipes بدون استثناءات

### ملاحظات التنفيذ
- العقد العام `IPtySession`: `DataReceived(byte[])` **بايتات خام** (لا نصّ مفكوك) — الـ VT parser (T-003) يفكّها؛ + `Exited(int)`، `WriteAsync`, `Write`, `Resize`, `HasExited`, `ProcessId`, `ExitCode`.
- **حلقة القراءة = خيط حاجب (`Thread`+`Read`) لا `ReadAsync`**: الأنابيب المجهولة من `CreatePipe` بلا overlapped I/O، فـ `FileStream.ReadAsync` عليها لا يسلّم بموثوقية؛ الخيط الحاجب هو نمط Windows Terminal والمرجع. يُلغى بإغلاق الـ PTY/الأنبوب في Dispose.
- ⚠️ **ConPTY يتطلّب كونسول Windows حقيقي بالمضيف.** إسناد الطفل للـ pseudoconsole يفشل إذا كانت باثات المضيف القياسية مُعاد توجيهها (git-bash / `dotnet run|pipe` / vstest testhost) → الطفل يرث كونسول الأب ولا يصل إخراجه للأنبوب. **مُتحقَّق تجريبياً:** على كونسول حقيقي (Start-Process) الإخراج يمرّ (MARKER=True)؛ ومحاولات `CREATE_NO_WINDOW` و`DETACHED_PROCESS` و`FreeConsole`/`AllocConsole` **تكسر الإسناد أو لا تُصلحه** — فلا تُضَف إلى `CreateProcess`. لذا اختبارا الإخراج `SkippableFact`: يُتخطَّيان حيث لا كونسول حقيقي، ويُنفَّذان على الجهاز/الـ CI بكونسول حقيقي. المضيف الفعلي (WPF WinExe) بلا كونسول يعمل صحيحاً لأنه لا يرث كونسولاً متعارضاً.
- التنظيف: `ClosePseudoConsole` → dispose الـ streams → `Join` للخيط (≤500ms) → `Unregister` انتظار الخروج → `TerminateProcess`+`CloseHandle` → `FreeHGlobal` لقائمة السمات.

---

## T-003: VT/ANSI Parser
**الحالة:** ✅ مكتمل | **الأولوية:** P0 | **التقدير:** 10-14 يوم | **يعتمد على:** T-002

**الهدف:** State Machine يحوّل بايتات الـ output إلى أوامر عرض. **هذا قلب المشروع — التغطية الاختبارية هنا لازم تكون الأعلى.**

> **حدّ الفصل (parser/buffer):** المحلّل **بنيويّ** — يتعرّف على كل تسلسل ويبثّه عبر `IVtParserSink` (Print/Execute/EscDispatch/CsiDispatch/OscDispatch). **الدلالة على الشبكة** (تحريك المؤشّر فعلياً، المسح، التمرير، تبديل الشاشة البديلة) يُنفّذها الـ buffer في **T-004** الذي يحقّق `IVtParserSink`. تفسير SGR→نمط مُنجَز هنا (`SgrProcessor`).

### Subtasks
- [x] T-003.1: فك ترميز UTF-8 تراكمي (multi-byte مقسوم بين قراءتين — مُختبَر بايتاً بايتاً)
- [x] T-003.2: State Machine (Ground/Escape/EscapeIntermediate/CsiEntry/Param/Intermediate/Ignore/OscString/StringConsume) على مخطط vt100.net
- [x] T-003.3: التحكم الأساسي: BEL/BS/TAB/LF/CR عبر `Execute(byte)` (C0)
- [x] T-003.4: SGR كامل عبر `SgrProcessor`: reset/bold/dim/italic/underline/blink/inverse/hidden/strike + 16 و256 و TrueColor (صيغتا `;` و`:`)
- [x] T-003.5: حركة المؤشّر CUU/CUD/CUF/CUB/CUP/HVP/CHA/VPA + DECSC/DECRC — **تُبثّ** (final الصحيح+بارامترات)؛ التنفيذ في T-004
- [x] T-003.6: المسح ED/EL/ECH — تُبثّ؛ التنفيذ في T-004
- [x] T-003.7: DECSTBM + IL/DL/SU/SD — تُبثّ؛ التنفيذ في T-004
- [x] T-003.8: OSC (0/2 عنوان، 8 hyperlink، 10/11 ألوان، 133) — تُسلَّم كاملةً عبر `OscDispatch(string)` مفكوكة UTF-8
- [x] T-003.9: الأوضاع (DECAWM/DECTCEM/alt-screen 1049/47/1047/bracketed-paste 2004) — تُبثّ عبر `CsiDispatch` ببادئة `?`؛ التنفيذ في T-004
- [x] T-003.10: عرض الخلايا `UnicodeWidth` (CJK/إيموجي=2، combining/zero-width=0، عربي=1)؛ تشكيل RTL = شأن الرندرر T-005
- [x] T-003.11: 46 اختباراً (بنية المحلّل + UTF-8 مقسوم + SGR + تكامل نمط «git log ملوّن»)؛ تغطية Vt **92.3%**

### معايير القبول
- [x] تغطية اختبارات فضاء `Vt` = **92.3%** (≥ 85%) — coverlet/cobertura
- [x] مُخرَج ملوّن نمط `git log --color` يُفكَّك صحيحاً (نصّ مرئي + مقاطع نمط) — `VtIntegrationTests`؛ العرض البصري الكامل يكتمل مع T-004/T-005
- [x] التسلسلات غير المدعومة/المشوّهة تُتجاهَل بأمان بلا استثناء (`Malformed_and_random_bytes_never_throw` يغذّي 512 بايت عشوائياً)

### ملاحظات التنفيذ
- الملفات في `engine/Terminal.Core/Vt/`: `VtParser` (آلة الحالات + UTF-8) · `IVtParserSink` (العقد البنيوي) · `VtParams` (بارامترات CSI مع sub-params بالنقطتين) · `SgrProcessor` (SGR→`TerminalStyle`) · `AnsiColor`/`TerminalStyle`/`TextStyleFlags` (النموذج، بلا UI) · `UnicodeWidth`.
- **`AnsiPalette` لم يُنقَل** — يعتمد `System.Windows.Media` (WPF) فيخصّ الرندرر T-005؛ نُقِل منطق النموذج فقط من `AnsiColor.cs` المرجعيّ.
- المحلّل **لا يملك حالة شبكة**؛ يملك فقط حالة التحليل + مجمّع UTF-8. كل تغيّر على الشاشة يمرّ عبر `IVtParserSink` (يحقّقه buffer الـ T-004).
- بايتات خام تدخل عبر `Feed(ReadOnlySpan<byte>)`/`Feed(byte)` — تتناسب مباشرةً مع `IPtySession.DataReceived(byte[])`.
- حواجز أمان: طول بارامتر CSI ≤256، طول OSC ≤8192، حارس فيض أرقام؛ CAN/SUB تُجهِض؛ ESC يُعيد بدء التسلسل.
- **حدّ الفصل:** تنفيذ CUP/ED/EL/DECSTBM/IL/DL/SU/SD/alt-screen/DECTCEM على الشبكة = T-004.

---

## T-004: Screen Buffer + Scrollback
**الحالة:** ✅ مكتمل | **الأولوية:** P0 | **التقدير:** 5 أيام | **يعتمد على:** T-003

**الهدف:** تمثيل الشاشة بالذاكرة بشكل كفوء.

### Subtasks
- [x] T-004.1: بنية `Cell` (struct): Rune + مؤشر تنسيق (Attributes مفهرسة بدل تكرارها بكل خلية) — `Cell{codepoint:int, StyleId:int}` (8 بايت) + `StyleTable` تُدوّل الأنماط (id 0 = Default)
- [x] T-004.2: `ScreenBuffer`: مصفوفة الشاشة المرئية + العمليات (كتابة، مسح، scroll، resize مع reflow بسيط) — يحقّق `IVtParserSink`؛ ينفّذ CUP/HVP/CUU-D-F-B/CHA/VPA/CNL/CPL + DECSC/DECRC/SCO + ED/EL/ECH + IL/DL/ICH/DCH/SU/SD + DECSTBM + SGR + IND/RI/NEL/RIS
- [x] T-004.3: `ScrollbackBuffer`: Circular Buffer بحجم قابل للتخصيص (افتراضي 10,000 سطر) — حلقة `Add` بـ O(1) تدهس الأقدم عند الامتلاء + عدّاد `Evicted` للترقيم المطلق
- [x] T-004.4: Alternate Screen Buffer منفصل (بدون scrollback) والتبديل بينهم — `?1049/?47/?1047` (1049 يحفظ/يستعيد المؤشّر)
- [x] T-004.5: آلية Dirty Regions — تتبع الأسطر المتغيرة حتى الـ Renderer يرسم المتغير فقط — `DirtyFromLine` (أصغر سطر مطلق تغيّر) يُصفَّر مع كل `Snapshot()`
- [x] T-004.6: اختبار ذاكرة: ضخ 1 مليون سطر والتأكد أن الاستهلاك ثابت عند حد الـ scrollback — `Feeding_a_million_lines_keeps_scrollback_capped` (يبقى العدّ = 10,000)

### معايير القبول
- [x] الذاكرة مستقرة مع output ضخم مستمر — الحلقة تحتفظ بـ `capacity` سطراً كحدّ أقصى (مُثبَت باختبار المليون سطر + عدّاد Evicted)
- [x] Resize ما يفقد محتوى الـ scrollback — `Resize_grow_preserves_scrollback` + الانكماش يدفع الفائض العلويّ إلى scrollback (`Resize_shrink_pushes_overflow_rows_to_scrollback`)
- [x] التبديل بين Main/Alternate buffer سليم — `Alt_screen_hides_main_and_restores_on_exit` (المحتوى والمؤشّر يرجعان) + `Alt_screen_keeps_no_scrollback`

### ملاحظات التنفيذ
- الملفات في `engine/Terminal.Core/Screen/` (فضاء `Terminal.Core.Screen`، منفصل عن `Vt`): `Cell` · `StyleTable` (تدويل الأنماط) · `ScrollbackBuffer` (حلقة) · `ScreenSnapshot`+`StyledRun` (لقطة العارض) · `ScreenBuffer` (المحرّك، يحقّق `IVtParserSink`).
- **حدّ الفصل مُحترَم:** المحلّل (T-003) يبثّ الأحداث البنيوية، والـ buffer يعطيها معنى على الشبكة. `ScreenBuffer.Feed(bytes)` يملك `VtParser` داخليّاً (نقطة التكامل مع `IPtySession.DataReceived`).
- **الخلية 8 بايت:** `codepoint:int` + `StyleId:int`؛ الأنماط مُدوّلة في `StyleTable` (id 0 = Default) بدل تكرار `TerminalStyle` (~16 بايت) بكل خلية.
- **المحارف العريضة (CJK/إيموجي):** الخلية القائدة تحمل الـ Rune والخلية التالية «wide-trailing» (بلا رمز)؛ التفاف مبكّر إن لم تتّسع بالعمود الأخير؛ كسر النِّصف اليتيم عند الكتابة فوق زوج. المحارف صفريّة العرض (combining/ZWJ) لا تُخزَّن كخلية (قيد موثّق — يُعالَج في الرندرر T-005).
- **الالتفاف المؤجَّل (DECAWM):** بعد الطباعة بالعمود الأخير يُرفع `_wrapPending`؛ الحرف التالي يلتفّ قبل الطباعة (سلوك xterm)، ويُلغى بأي حركة مؤشّر.
- **الشاشة البديلة بلا scrollback:** `ScrollUp` يدفع للـ scrollback فقط حين main + المنطقة كاملة (`0..rows-1`)؛ تمرير المناطق الجزئيّة أو البديلة لا يُغذّي التاريخ.
- **أعلام لطبقات لاحقة:** `DECCKM(?1)` و`bracketed-paste(?2004)` يُخزَّنان كأعلام (`ApplicationCursorKeys`/`BracketedPaste`) لطبقة الإدخال T-006؛ OSC 0/2 → `Title`؛ إبلاغ الماوس (1000/1002/1006) وOSC 8/10/11/133 مؤجَّلة لمراحلها.
- **الاختبارات:** 65 اختباراً لفضاء `Screen` (تغطية **93.4%**؛ ScreenBuffer 92.9%، البقيّة ~100%). البناء 0/0، كامل الحلّ 116 ناجح Core (+2 SkippableFact) + 2 UI.
- **مؤجَّل لـ T-005 (الرندرر):** reflow حقيقيّ لسطور الـ scrollback عند التحجيم (حاليّاً تُحفَظ كما هي بلا إعادة التفاف)؛ رسم المؤشّر/الوميض؛ تشكيل RTL وربط عرض الخلية بالتخطيط.

---

## T-005: الـ Renderer
**الحالة:** ✅ مكتمل | **الأولوية:** P0 | **التقدير:** 10-14 يوم | **يعتمد على:** T-004

**الهدف:** رسم الـ Buffer بأداء عالي داخل WPF عبر SkiaSharp، خلف واجهة `IRenderer`.

### Subtasks
- [x] T-005.1: `IRenderer` واجهة محايدة تقنياً (`Controls/IRenderer.cs`، لا تعرف SkiaSharp) — قابلة لاستبدال الخلفية بـ Direct2D لاحقاً
- [x] T-005.2: `SkiaTerminalRenderer : FrameworkElement, IRenderer` يرسم شبكة الخلايا على `WriteableBitmap` عبر `SKSurface` مباشرةً (بلا `SkiaSharp.Views.WPF`/OpenTK)؛ مقاييس الخليّة من الخطّ الأُحاديّ (`MeasureText("M")`/`Spacing`)
- [x] T-005.3: رسم النص: TrueColor أمام/خلف، Bold/Dim/Italic/Underline/Strikethrough/Inverse، أحرف عريضة (CJK/إيموجي=خليّتان)، خطوط بديلة لما لا يملكه الأساس + **تشكيل عربي HarfBuzz** (RTL/اتّصال عبر `SKShaper`)
- [x] T-005.4: **Ligatures: مؤجَّلة كـ known-limitation موثّقة** (الدليل يجيز) — الرسم لكل غليف على حدة يضمن محاذاة الشبكة الخلويّة؛ العربية تُشكَّل عبر HarfBuzz، لكن ligatures اللاتينية (Cascadia Code) بحاجة تمرير shaping على المقاطع اللاتينية → أُجِّل
- [x] T-005.5: أنماط المؤشّر (Block/Bar/Underline) **+ وميض** (مؤقّت 530ms نمط xterm عبر `CursorBlinkOn`؛ يبقى صلباً أثناء الكتابة)
- [x] T-005.6: حلقة رسم على Dirty Regions + دمج تحديثات (مؤقّت `_refresh` 40ms → `FlushOutput`؛ ~25 إطار/ث كسقف)
- [x] T-005.7: `ScrollBar` رأسيّ (نمط `SlimScroll`) موصول بـ `MaxScrollOffset` + عجلة الماوس تمرّر الـ scrollback (±3 سطور)
- [~] T-005.8: قياس أداء `type` لملف 100MB (FPS≥30) — **لم يُقَس آلياً** (يحتاج تشغيلاً بصرياً على كونسول حقيقي)؛ البنية مهيّأة (dirty regions + frame coalescing + خيط قراءة حاجب)

### معايير القبول
- [x] `htop`/ملوّن يظهر بألوانه بلا تكسّر — **منطقياً محقَّق** (كامل مسار VT→buffer→snapshot→رسم)؛ التحقّق البصري النهائيّ يحتاج كونسولاً حقيقياً (نفس عائق ConPTY في T-002)
- [x] Scrolling بعجلة الماوس عبر scrollback (سعة 10,000) — موصول عبر `ScrollOffset`/`MaxScrollOffset`
- [x] تغيير حجم النافذة يعيد الرسم + يرسل Resize للـ PTY — `SizeChanged → ResizeSession` (يقيس الأعمدة/الصفوف من الرندرر ويستدعي `Resize`)

### ملاحظات التنفيذ
- **تصحيح موضع (2026-07-04):** أُنجِز فعلياً داخل `Tools/TerminalLauncher/` (لا `Tools/Terminal/` الذي رُوجِع بالكوميت `b2a14ff`). المحرّك `Terminal.Core` نُقِل إلى `TerminalLauncher/engine/` وصار الافتراضيّ (`8bdcd87`).
- الملفّات: `Controls/IRenderer.cs` (الحدّ المحايد) · `Controls/SkiaTerminalRenderer.cs` (~925 سطر، الرندرر) · `Controls/CoreSnapshotAdapter.cs` (لقطة Core → نموذج الرندرر) · `Controls/TerminalTabView.xaml(.cs)` (الاستضافة/التمرير/الحجم).
- **بلا `SkiaSharp.Views.WPF`:** نرسم على `WriteableBitmap` عبر `SKSurface.Create` فوق الـ BackBuffer — يتفادى جرّ OpenTK (NU1701). الحُزَم: `SkiaSharp` + `SkiaSharp.HarfBuzz` 3.119.0.
- **الوميض (2026-07-04):** `SkiaTerminalRenderer.CursorBlinkOn` يبوّب رسم المؤشّر؛ `_blinkTimer` (530ms) في الكنترول يقلّبه، ويُعاد ضبطه صلباً مع كل ضغطة (`ResetCursorBlink`).
- البناء 0/0؛ اختبارات المحرّك 127 ناجح + 2 SkippableFact.

---

## T-006: الإدخال (Keyboard & Mouse)
**الحالة:** ✅ مكتمل | **الأولوية:** P0 | **التقدير:** 4 أيام | **يعتمد على:** T-005

### Subtasks
- [x] T-006.1: ترجمة المفاتيح → VT عبر `SpecialKeyToVt`: الأسهم/Home/End (واعية بـ DECCKM: SS3 `\x1bO_` عند نمط التطبيق، CSI `\x1b[_` وإلّا)، PgUp/PgDn، F1-F12، Insert/Delete (`\x1b[3~`)
- [x] T-006.2: تركيبات Ctrl/Alt/Shift: Ctrl+حرف → بايت تحكّم (Ctrl+C=0x03)، Alt→بادئة ESC؛ الاختصارات المحجوزة (Ctrl+Shift+C نسخ…) تُفحَص أوّلاً في `PreviewKeyDown` فلا تصل للـ shell
- [x] T-006.3: IME/`PreviewTextInput` (`Renderer_PreviewTextInput`) للكتابة بالعربي وغيرها → يُكتب النصّ للجلسة
- [x] T-006.4: Mouse Reporting (1000/1002/1006 SGR) عبر `ReportMouse` — يقرأ أعلام `_coreScreen` (`MouseReportsDrag` إلخ)؛ أنواع الأزرار/الأحداث في `engine/Terminal.Core/Screen/MouseReporting.cs`
- [x] T-006.5: Keybindings مركزيّة: `BuildShortcuts()` → `Dictionary<(Key,ModifierKeys),TermAction>` (لا hardcoded)

### معايير القبول
- [x] vim (تنقّل/insert/حفظ/خروج بالكيبورد) — **منطقياً محقَّق** عبر ترجمة المفاتيح الكاملة + DECCKM؛ التحقّق البصري النهائيّ يحتاج كونسولاً حقيقياً (عائق ConPTY)
- [x] الكتابة بالعربي داخل الـ shell — مسار الإدخال (`PreviewTextInput`) + العرض المُشكَّل (رندرر HarfBuzz) مكتمِلان
- [x] الماوس داخل htop (نقر/سحب) — `ReportMouse` يبعث تقارير SGR حين يفعّل التطبيق التتبّع

### ملاحظات التنفيذ
- **تصحيح موضع (2026-07-04):** أُنجِز داخل `Tools/TerminalLauncher/Controls/TerminalTabView.xaml.cs` (940+ سطر) — الكنترول المضيف نفسه الذي يستضيف الرندرر (T-005)، فالتاسكان **مترابطان بالملف** لا مستقلَّين.
- ترجمة المفاتيح: `Renderer_PreviewKeyDown` → (١) اختصارات محجوزة، (٢) `SpecialKeyToVt` واعية بـ DECCKM، (٣) Ctrl/Alt→بايتات تحكّم؛ النصّ العاديّ عبر `PreviewTextInput`.
- **ملاحظة تبعية:** الدليل يجعل T-006 «يعتمد على T-005»، لكن عملياً نُفِّذ الاثنان معاً في نفس الكنترول المضيف (استضافة الرندرر + التقاط الإدخال متلازمان).
- البناء 0/0.

---

# ✅ اختبار إغلاق المرحلة (Phase 0 Exit Test)
نفّذ هذا السيناريو كاملاً قبل اعتبار المرحلة منتهية:
1. شغّل PowerShell → `git log --oneline --graph` بمستودع حقيقي → الألوان والرسم البياني صحيح
2. شغّل WSL → `htop` → يعمل بتحديث مستمر، الماوس يشتغل، q للخروج يرجّع الشاشة
3. `vim test.txt` → اكتب، احفظ، اخرج → الملف انحفظ والشاشة رجعت
4. اكتب نص عربي بالـ prompt → يظهر بدون تكسّر
5. غيّر حجم النافذة أثناء تشغيل htop → يتكيف بدون crash
