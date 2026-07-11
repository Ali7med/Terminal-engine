# 🔀 خريطة النقل — من الكود الحالي إلى معمار الدليل

> **القرار (2026-07-03):** تبنّي معمار [`AGENT_GUIDE.md`](../tasks/AGENT_GUIDE.md) **مع نقل المنطق الجاهز** (الخيار A).
> هذه الوثيقة تربط كل ملفّ في الكود الحالي (مشروع WPF واحد) بموضعه في المعمار الجديد وبتاسك الدليل المقابل.
> **جلسة العمل تبدأ من `T-001` وتستعمل هذا الجدول كمرجع نقل — لا نسخ أعمى، بل موائمة على العقود الجديدة.**
> **الموضع (مُصحَّح 2026-07-04):** لم يعد ثمّة مشروع منفصل — مجلد `Tools/Terminal/` **رُوجِع** (الكوميت `b2a14ff`) واندمج كلّ شيء في **`Tools/TerminalLauncher/`**: المحرّك المنقول تحت `engine/Terminal.Core`، والـUI (الرندرر/الإدخال) تحت `Controls/`. مسارات عمود «الكود الحالي» أدناه تشير إلى كود WPF المفرد **الذي نُقِل منه** داخل نفس المشروع.

---

## المبدأ

- **مُحرّك نقيّ بلا UI**: منطق ConPTY/الـparser/الـbuffer يُنقل إلى `Terminal.Core` (بلا أي مرجع WPF/SkiaSharp).
- **الرندرر يُعاد بناؤه**: طبقة العرض الحالية (`RichTextBox`/`FlowDocument`) لا تُنقل — تُستبدل بـ SkiaSharp خلف `IRenderer`. الكود الحالي مرجع **لمنطق التخطيط/الـdirty** فقط.
- **التخزين يُستبدل**: `JSON` الحالي (`EntryStore`/`SettingsStore`) → `SQLite` (+JSON للإعدادات) حسب الدليل. الكود الحالي مرجع للـschema المنطقيّ.
- **العقود الجديدة تُقدَّم**: عند وجود تعارض بين الكود الحالي وعقد الدليل (مثل فصل الـParser عن الـBuffer، أو `Cell` struct بمؤشّر تنسيق مفهرس)، **عقد الدليل يُغلَّب** والكود الحالي يُقتبَس منطقُه فقط.

---

## جدول النقل

| التاسك | المشروع الهدف | الكود الحالي (مرجع) | استراتيجية النقل |
|---|---|---|---|
| **T-002** ConPTY | `Terminal.Core` | `Terminal/PseudoConsoleSession.cs` | **نقل مباشر تقريباً** — P/Invoke + pipes + read loop + Resize + Dispose جاهزة. أعِد تسميتها `PtySession` وافصل `NativeMethods.cs`، وحوّل الحدث إلى `DataReceived(byte[])` async. |
| **T-003** VT/ANSI Parser | `Terminal.Core` | `Terminal/TerminalScreen.cs` (منطق CSI/SGR/OSC) + `Terminal/AnsiColor.cs` + `Terminal/AnsiPalette.cs` | **اقتباس منطق + إعادة هيكلة**. الحالي يخلط الـparser بالـbuffer؛ الدليل يفصلهما (State Machine على مخطط vt100.net). انقل جداول SGR/الألوان (16/256/truecolor) مباشرةً؛ أعِد بناء الـstate machine نظيفاً مع UTF-8 تراكمي وتغطية اختبار ≥85%. |
| **T-004** Screen Buffer | `Terminal.Core` | `Terminal/TerminalScreen.cs` (الشبكة 2D/alt-screen/scroll-regions/scrollback) | **اقتباس منطق قويّ**. الشبكة الثنائية + alt-screen + منطقة التمرير + scrollback + dirty-tracking كلها موجودة كمنطق. أعِد صياغتها على `Cell` struct (Rune + مؤشّر تنسيق مفهرس) و`Circular Buffer`. |
| **T-005** Renderer | `Terminal.UI` | `Controls/TerminalDocumentRenderer.cs` | **إعادة بناء كامل على SkiaSharp** (لا نقل). الحالي `RichTextBox`؛ يُستبدل بـ `SKElement` يرسم شبكة الخلايا. يُقتبَس منه فقط: منطق الـdirty-regions والالتصاق بالأسفل والاقتطاع. |
| **T-006** Input | `Terminal.UI` | `Controls/TerminalTabView.xaml.cs` (خرائط المفاتيح) | **اقتباس مباشر**. ترجمة المفاتيح→VT (أسهم/Home/End/PgUp-Dn/Del)، Ctrl+A..Z→0x01..1A، Ctrl+C نسخ-أو-0x03، نسخ-عند-التحديد/لصق-بالأيمن جاهزة. أضِف IME عربي + Mouse Reporting (1000/1002/1006) + keybindings dictionary. |
| **T-101** Shell Profiles | `Terminal.Core`/`UI` | `Terminal/ShellCatalog.cs` + `Models/CommandEntry.cs` | **اقتباس**. cmd/PowerShell/Git-Bash + كشف مسار bash جاهز. أضِف WSL + executables مخصّصة + `IShell`. |
| **T-102** Tabs | `Terminal.UI` | `MainWindow.xaml` (`TabControl`) | مرجع تخطيط؛ يُعاد ضمن MVVM/DI. |
| **T-103** Split Panes | `Terminal.UI` | `Controls/TerminalPaneContainer.cs` | **اقتباس منطق** — شجرة الأجزاء + GridSplitter + توجيه الجزء النشط جاهزة. |
| **T-104** Copy/Paste | `Terminal.UI` | `TerminalTabView.xaml.cs` | جاهز (نسخ-عند-التحديد + لصق-بالأيمن). أضِف حماية اللصق متعدّد الأسطر. |
| **T-105** Search | `Terminal.UI` | `Controls/DocumentSearch.cs` | **اقتباس** — بحث + تظليل + تنقّل. يُعاد على buffer المفهرس بدل الـFlowDocument. |
| **T-107** Themes/Fonts | `Terminal.UI` | `Theme/ThemeManager.cs` + `Theme/AppSettings.cs` + `Styles/` | **اقتباس** — مود×لكنة + خطوط + zoom جاهزة. |
| **T-108/T-109** Config/Sessions | `Terminal.Core` | `Services/EntryStore.cs` + `Services/SettingsStore.cs` (JSON) | **يُستبدل بـ SQLite**؛ الحالي مرجع للـschema المنطقيّ فقط. |
| **T-110** Clickable Links | `Terminal.Core`/`UI` | (OSC handling في `TerminalScreen.cs`) | يُبنى على OSC 8 hyperlinks — البنية موجودة. |
| **T-201** Command Palette | `Terminal.UI` | `Models/CommandPaletteItem.cs` + `MainWindow.xaml(.cs)` | **اقتباس** — palette + fuzzy filter + تنقّل جاهزة. |
| **T-207** Command Blocks | `Terminal.Core`/`UI` | OSC 133 في `TerminalScreen.cs` + تزيين `TerminalDocumentRenderer.cs` | **اقتباس منطق** — نموذج الكتل (OSC133 + استدلال) + القفز + النسخ جاهزة. تُعاد على الرندرر الجديد. |
| **T-401** AI Infrastructure | `Terminal.Ai` | `Services/AiAssistant.cs` (stub معطّل) | **اقتباس الواجهة** — `IAiAssistant`/`AiBlockContext` + علَم `AiAssistantEnabled` جاهزة كنقطة انطلاق لـ `IAiProvider`. |
| **إطار النافذة** | `Terminal.UI` | `Interop/WindowEffects.cs` + `MainWindow` (WindowChrome/الزوايا/الظلّ) | **اقتباس** — إطار مخصّص + زوايا مستديرة + ظلّ جاهزة. |

---

## بلا مرجع حالي (تُبنى من الصفر)

هذه لا يوجد لها كود حالي ويجب بناؤها كاملةً حسب تاسكاتها:
- **البنية التحتية**: `T-001` (حلّ متعدّد المشاريع + DI Host + xUnit + `Directory.Build.props`/`.editorconfig`).
- **Phase 2 المتبقّي**: Quake Mode، Snippets، Autocomplete، Smart History، Project Profiles، Env Vars، Broadcast، Notifications.
- **Phase 3 كاملةً**: SSH/SFTP/Port-Forwarding/Docker/Git/DB/أدوات الشبكة/System Monitor/Logs/Recording.
- **Phase 4**: Explain Error/NL→Command/Explain Command/AI Panel (فوق `Terminal.Ai`).
- **Phase 5**: Plugin System/Macro Recorder/ERP via MCP.

---

## قرار T-001 (محسوم — 2026-07-03)

- **موضع الحلّ**: ~~فولدر جديد `Tools/Terminal/` بحلّ منفصل~~ **← متجاوَز (2026-07-04):** رُوجِع (`b2a14ff`) واندمج في **`Tools/TerminalLauncher/`** — المحرّك تحت `engine/Terminal.Core` (بـ `engine/Directory.Build.props` يعزله عن شجرة المنصّة) والـUI = مشروع `TerminalLauncher` الجذر. لا `src/`؛ الاختبارات في `engine/Terminal.Core.Tests`.
- **الإطار المستهدف**: ✅ **.NET 10** (يطابق المنصّة والأداة الحالية).
- **مصير المشروع الحالي**: يبقى كـ«مرجع نقل حيّ» بجانب الحلّ الجديد حتى تُنقل كل نقاط الجدول أعلاه، ثم يُتقاعَد — **لا يُحذف قبل اكتمال Phase 0 Exit Test على المعمار الجديد**.
- **الدليل القاطع**: نسخة واحدة `tasks/AGENT_GUIDE.md` (أُزيلت نسخة `docs/` المكرّرة).
