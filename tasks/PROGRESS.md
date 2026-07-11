# 📊 تتبع التقدم — مشروع التيرمنال
> **الوكيل يحدّث هذا الجدول بعد كل تاسك.** الحالات: ⬜ لم يبدأ | 🔄 قيد العمل | ✅ مكتمل | ⏸️ معلّق (وثّق السبب) | 🚫 مؤجل
>
> ### 🔒 قفل الجلسات (إلزامي — لمنع تعارض الجلسات/العملاء المتوازية)
> عمود **«الجلسة»** يحمل معرّف الجلسة العاملة على التاسك (مثال: `731f170d`). البروتوكول:
> 1. **قبل البدء:** تأكّد أنّ خانة «الجلسة» **فارغة**. إن كانت مملوءة فالتاسك **محجوز لجلسة أخرى** — لا تعمل عليه إطلاقاً واختر تاسكاً آخر متاحاً.
> 2. **عند البدء:** اكتب معرّف جلستك في الخانة، بدّل الحالة إلى 🔄، واحفظ الملف **فوراً قبل الشروع في العمل** (كي تراه الجلسات الأخرى).
> 3. **عند الإكمال:** بدّل الحالة إلى ✅ و**امسح** خانة الجلسة (يعود التاسك متاحاً للأرشفة/المتابعة).
> 4. **إن توقّفت دون إكمال:** بدّل الحالة إلى ⏸️، **امسح** خانة الجلسة، ووثّق السبب في «ملاحظات» — كي يتحرّر التاسك لغيرك.
> 5. **قاعدة قاطعة:** لا تلمس أبداً تاسكاً خانةُ جلسته مملوءة بمعرّف جلسة أخرى حتى تُكمله تلك الجلسة وتُفرّغه.

**التاسك الحالي:** Phase 1 مكتمل (عدا Exit Test) — التالي المتاح: T-202 Quake / T-204 Snippets / T-212 Sessions Sidebar
**آخر تحديث:** 2026-07-05

> **استراتيجية معتمَدة (2026-07-03): تبنّي معمار الدليل + نقل المنطق الجاهز (الخيار A).**
> يوجد كود WPF حاليّ شغّال (مشروع واحد/RichTextBox/JSON) يغطّي منطق ConPTY/VT-parser/buffer/splits/palette/blocks. هذا الكود **مرجع نقل لا حالة إنجاز** — لا يحقّق معايير قبول الدليل الجديد (SkiaSharp/فصل طبقات/SQLite/اختبارات)، لذا كل التاسكات تبقى ⬜ حتى تُنقل وتُختبَر على المعمار الجديد.
> خريطة النقل (كود حاليّ → تاسك) في [`../docs/PORTING_MAP.md`](../docs/PORTING_MAP.md). عمود «ملاحظات» أدناه يحمل مؤشّر المرجع لكل تاسك له كود حاليّ.
> **الموضع (مُصحَّح 2026-07-04):** مجلد `Tools/Terminal/` المنفصل **رُوجِع** (الكوميت `b2a14ff`) واندمج المحرّك في **`Tools/TerminalLauncher/engine/Terminal.Core`** وصار الافتراضيّ (`8bdcd87`). العمل الفعليّ لـ Phase 0 كلّه داخل `TerminalLauncher/` (الرندرر في `Controls/SkiaTerminalRenderer.cs`، الإدخال في `Controls/TerminalTabView.xaml.cs`). ملاحظة الاستراتيجية أعلاه (كل التاسكات ⬜) **متجاوَزة**: T-001…T-006 مُنجَزة ومُختبَرة فعلاً.

## Phase 0 — الأساس التقني
| ID | التاسك | الأولوية | الحالة | الجلسة | ملاحظات |
|---|---|---|---|---|---|
| T-001 | بنية المشروع | P0 | ✅ |  | داخل `Tools/TerminalLauncher/` (.NET 10): UI = مشروع `TerminalLauncher` الجذر + `engine/{Terminal.Core, Terminal.Core.Tests, Terminal.Plugins.Sdk}`؛ DI Generic Host؛ `engine/Directory.Build.props` يعزل المحرّك؛ بناء 0 تحذير؛ layering test يمنع مرجع UI في Core |
| T-002 | ConPTY | P0 | ✅ |  | `Core/Pty/PtySession`+`NativeMethods`+`IPtySession` (بايتات خام DataReceived، read-thread حاجب)؛ مُتحقَّق حيّاً على كونسول حقيقي (MARKER=True)؛ اختبارا الإخراج SkippableFact (يحتاجان كونسول حقيقي) |
| T-003 | VT Parser | P0 | ✅ |  | `Core/Vt/`: `VtParser` (state machine vt100.net + UTF-8 تراكمي) → `IVtParserSink` بنيوي؛ `SgrProcessor` (16/256/truecolor سطر+نقطتان)؛ `VtParams`/`AnsiColor`/`TerminalStyle`/`UnicodeWidth`؛ تغطية Vt **92.3%** (46 اختبار)؛ **تنفيذ المؤشّر/المسح/التمرير/الأوضاع على الشبكة = T-004** |
| T-004 | Screen Buffer | P0 | ✅ |  | `Core/Screen/`: `ScreenBuffer` يحقّق `IVtParserSink` (CUP/ED/EL/ECH/IL/DL/ICH/DCH/SU/SD/DECSTBM/IND/RI/NEL/RIS + alt-screen ?1049/?47/?1047)؛ `Cell` 8-بايت + `StyleTable` (تدويل)؛ `ScrollbackBuffer` حلقة O(1) (افتراضي 10k)؛ dirty عبر `DirtyFromLine`+`Snapshot()`؛ محارف عريضة/التفاف مؤجَّل؛ تغطية Screen **93.4%** (65 اختبار، اختبار مليون سطر) |
| T-005 | Renderer | P0 | ✅ |  | مُنجَز في `TerminalLauncher/`: `Controls/SkiaTerminalRenderer.cs` (SkiaSharp على WriteableBitmap بلا Views.WPF): TrueColor/أنماط/أحرف عريضة/HarfBuzz عربي/مؤشّر بأنماطه **+ وميض 530ms**/dirty-40ms/ScrollBar+عجلة. ligatures لاتينية مؤجَّلة (known-limitation). البناء 0/0 |
| T-006 | Input | P0 | ✅ |  | مُنجَز في `Controls/TerminalTabView.xaml.cs`: مفاتيح→VT واعية DECCKM (SS3/CSI، F1-F12، Ins/Del)، Ctrl/Alt، IME عربي (`PreviewTextInput`)، mouse reporting SGR (1000/1002/1006)، اختصارات مركزية Dictionary. **مترابط بالملف مع T-005** لا مستقلّ |
| — | **Phase 0 Exit Test** | — | ⬜ |  | |

## Phase 1 — MVP
| ID | التاسك | الأولوية | الحالة | الجلسة | ملاحظات |
|---|---|---|---|---|---|
| T-101 | Shell Profiles | P0 | ✅ |  | `Models/ShellProfile.cs` + `Services/ShellDetector.cs` (كشف CMD/PS5/pwsh/GitBash/WSL عبر `wsl -l -q` مع UTF-16/8) + `Services/ProfileStore.cs` (profiles.json) + dropdown بزرّ «+» + إدارة بروفايلات + تمرير WorkingDir/Env. الترتيب/الأيقونات/TabColor مؤجَّلة (UI) |
| T-102 | Tabs | P0 | ✅ |  | مُنجَز: `MainWindow.xaml` `TabControl x:Name=TerminalTabs`؛ كل تبويب `TerminalPaneContainer`؛ زرّ «+»/قائمة بروفايلات، عنوان تبويب، إغلاق. حفظ/استعادة عبر T-109 |
| T-103 | Split Panes | P0 | ✅ |  | مُنجَز: `Controls/TerminalPaneContainer.cs` + `MainWindow.SplitActivePane`/`CloseActivePane` (Ctrl+Shift+D عمودي، Ctrl+Shift+E أفقي)؛ قابل للتحجيم (GridSplitter مخصّص + أحجام دنيا) وحدود أخفّ للأجزاء المنقسمة (كوميت f56384f/fb86109) |
| T-104 | Copy/Paste + حماية | P0 | ✅ |  | `Controls/PasteConfirmDialog.xaml(.cs)`: تحذير قبل لصق متعدّد الأسطر أو أنماط خطرة (`rm -rf`/`del /s`/`format`/`Remove-Item -Recurse`/`dd if=`) + Ctrl+V/Bracketed(2004) + Ctrl+Shift+C + double/triple-click كلمة/سطر. Alt+drag مستطيل مؤجَّل |
| T-105 | البحث | P0 | ✅ |  | مُنجَز: `TerminalTabView` شريط بحث (`SearchBar`/`SearchInput` + Prev/Next/Close، Ctrl+F فتح، Enter/Shift+Enter تنقّل، Esc إغلاق) + `SkiaTerminalRenderer.SetSearchMatches` يبرز المطابقات على الـsnapshot (الحاليّة بلون أقوى) |
| T-106 | History | P1 | ✅ |  | مكتمل: `CommandHistoryStore` (SQLite، 11 اختبار) + التقاط الأوامر (مسار الأمر المحفوظ + كتل OSC 133 مع dedup) + زرّ 🕘 تاريخ في `TerminalTabView` (Popup/ListBox، نقر مزدوج→إرسال) + مفتاح `tip.history` (en/ar) |
| T-107 | ثيمات وخطوط | P1 | ✅ |  | مُنجَز: نظام قوالب ثيم (`ThemeCardPanel` presets) + إعدادات كامل الشاشة بفئتين (المظهر+الخلفية، اللغة+الخط) + صور خلفية/شفافية التيرمنال + لون نصّ افتراضيّ. خطّ الواجهة `Font.Ui` DynamicResource (كوميتات b21e5c7/47127d1/de49f13/bb54bed) |
| T-108 | Config File | P1 | ✅ |  | مكتمل: `Terminal.Storage/SettingsSqliteStore` (key-value، upsert) + `Services/SettingsStore` يفوّض له مع هجرة `settings.json`→SQLite لمرّة واحدة (يُعاد تسميته `.migrated`) + 12 اختبار. الواجهة العامّة بلا تغيير |
| T-109 | Sessions | P1 | ✅ |  | مكتمل: `SessionStore` (SQLite، 8 اختبار) + `MainWindow`: `OnClosing`→حفظ لقطة التبويبات المرتّبة، `Loaded`→استعادة تلقائية (حارس HasItems). التبويب يحمل `CommandEntry` في `Tag` |
| T-110 | Clickable Links | P1 | ✅ |  | `Services/LinkDetector.cs` (كشف URL/مسار/`file:line` على السطر) + `Services/LinkOpener.cs` (فتح آمن: متصفح/Explorer/محرّر، `code -g file:line`) + Ctrl+Click بالماوس + مؤشّر يد عند Ctrl+Hover + أولويّة OSC 8 الصريح. تسطير المكتشَف عند hover مؤجَّل |
| — | **Phase 1 Exit Test** | — | ⬜ |  | |

## Phase 2 — الإنتاجية
| ID | التاسك | الأولوية | الحالة | الجلسة | ملاحظات |
|---|---|---|---|---|---|
| T-201 | Command Palette | P0 | ✅ |  | مُنجَز: `MainWindow` `CommandPaletteOverlay` (Ctrl+P فتح، Esc إغلاق) + `BuildPaletteSource` أوامر فعليّة (تبويب/انقسام/إعدادات…) + `FilterPalette` بحث حيّ + `CommandPaletteList` نقر مزدوج تنفيذ، فوق `Models/CommandPaletteItem.cs` |
| T-202 | Quake Mode | P1 | ⬜ |  | بلا مرجع |
| T-203 | Quick Commands | P1 | ✅ |  | مُنجَز: شريط جانبيّ «الأوامر المحفوظة» في `MainWindow` (`EntryStore` + `CommandEntry` Name/Path) + زرّ «＋» إضافة + قابل للطيّ/التوسيع (`ToggleSidebarExpanded`) + فتح تبويب من مدخلة محفوظة |
| T-204 | Snippets | P1 | ⬜ |  | بلا مرجع |
| T-205 | Autocomplete | P1 | ✅ |  | مُنجَز: إكمال سطريّ تعلّميّ بنصّ شبح (ghost text) في `TerminalTabView.UpdateGhost` عبر `_history.Suggest(prefix)` → `Renderer.GhostText` + قبول بـ Tab/Right (`TryAcceptGhost`) (كوميت 92b9503) |
| T-206 | Smart History + ★ | P1 | ⬜ |  | بلا مرجع |
| T-207 | Command Blocks | P2 | ✅ |  | مُنجَز: التقاط OSC 133 (كتل في `ScreenSnapshot.Blocks` مع حالة Success/Failed/Running) + `SkiaTerminalRenderer.DrawBlocks` شريط رأسيّ ملوّن بالحالة + تنقّل الكتل Ctrl+Up/Down (`JumpBlock`) + نسخ الكتلة Ctrl+Shift+C (`CopyBlock`) |
| T-208 | Project Profiles | P1 | ⬜ |  | بلا مرجع |
| T-209 | Env Vars Editor | P2 | ⬜ |  | بلا مرجع |
| T-210 | Broadcast | P2 | ⬜ |  | بلا مرجع |
| T-211 | Notifications | P2 | ⬜ |  | بلا مرجع |
| T-212 | Sessions Sidebar | P1 | ⬜ |  | مرجع تخطيط: الشريط الجانبي في `MainWindow.xaml` |
| — | **Phase 2 Exit Test** | — | ⬜ |  | |

## Phase 3 — الاحترافية
| ID | التاسك | الأولوية | الحالة | الجلسة | ملاحظات |
|---|---|---|---|---|---|
| T-301 | SSH Manager | P0 | ⬜ |  | |
| T-302 | Security Suite | P0 | ⬜ |  | |
| T-303 | SFTP | P1 | ⬜ |  | |
| T-304 | Port Forwarding | P2 | ⬜ |  | |
| T-305 | Docker Panel | P1 | ⬜ |  | |
| T-306 | Git Panel | P1 | ⬜ |  | |
| T-307 | DB Client | P2 | ⬜ |  | |
| T-308 | أدوات الشبكة | P2 | ⬜ |  | |
| T-309 | System Monitor | P2 | ⬜ |  | |
| T-310 | Logs Viewer | P2 | ⬜ |  | |
| T-311 | Session Recording | P2 | ⬜ |  | |
| T-312 | مشاركة الجلسة | P3 | 🚫 |  | مؤجل — يحتاج Backend |
| — | **Phase 3 Exit Test** | — | ⬜ |  | |

## Phase 4 — الذكاء الاصطناعي
| ID | التاسك | الأولوية | الحالة | الجلسة | ملاحظات |
|---|---|---|---|---|---|
| T-401 | AI Infrastructure | P0 | ⬜ |  | مرجع واجهة: `Services/AiAssistant.cs` (stub) → `IAiProvider` في `Terminal.Ai` |
| T-402 | Explain Error | P0 | ⬜ |  | |
| T-403 | NL → Command | P1 | ⬜ |  | |
| T-404 | Explain Command | P1 | ⬜ |  | |
| T-405 | AI Panel | P2 | ⬜ |  | |
| — | **Phase 4 Exit Test** | — | ⬜ |  | |

## Phase 5 — التميّز
| ID | التاسك | الأولوية | الحالة | الجلسة | ملاحظات |
|---|---|---|---|---|---|
| T-501 | Plugin System | P0 | ⬜ |  | فوق `Terminal.Plugins.Sdk` |
| T-502 | Macro Recorder | P1 | ⬜ |  | |
| T-503 | ERP via MCP | P1 | ⬜ |  | |
| T-504 | Store | P3 | 🚫 |  | مؤجل |
| — | **Phase 5 Exit Test** | — | ⬜ |  | |

## 📝 سجل القرارات التقنية (يحدّثه الوكيل)
| التاريخ | القرار | السبب |
|---|---|---|
| 2026-07-03 | تبنّي معمار الدليل **مع نقل المنطق الجاهز** (الخيار A) | تحقيق معمار الدليل الاحترافي دون إهدار أصعب الأجزاء (ConPTY/VT-parser/buffer). خريطة النقل: `docs/PORTING_MAP.md` |
| 2026-07-03 | ~~موضع الحلّ: فولدر جديد `Tools/Terminal/` بحلّ منفصل~~ **← متجاوَز (انظر 2026-07-04)** | كان: فولدر مستقلّ عن `TerminalLauncher`. **رُوجِع** لاحقاً واندمج المحرّك في `Tools/TerminalLauncher/engine/` |
| 2026-07-03 | الإطار: **.NET 10** | يطابق المنصّة والأداة الحالية |
| 2026-07-03 | الدليل القاطع: نسخة واحدة `tasks/AGENT_GUIDE.md` | أُزيلت نسخة `docs/` المكرّرة (كانت .NET 9/جذر csproj) |
| 2026-07-03 | الكود الحالي = مرجع نقل لا حالة إنجاز؛ كل التاسكات ⬜ | لا يحقّق معايير قبول الدليل (SkiaSharp/طبقات/SQLite/اختبارات) |
| 2026-07-03 | Renderer: SkiaSharp خلف IRenderer | أسهل بداية، قابل للاستبدال (Direct2D مستقبلاً) — الرندرر الحالي RichTextBox يُستبدل لا يُنقل |
| 2026-07-03 | DB: SQLite لكل التخزين المحلي | ملف واحد، بدون سيرفر — يستبدل JSON الحالي |
| 2026-07-03 | الأسرار: DPAPI حصراً | مربوطة بحساب Windows |
| 2026-07-03 | **الموجة الأولى: T-001 + T-002 مكتملان** | الأساس (حلّ+DI+اختبارات) + محرّك ConPTY حقيقي؛ بناء 0/0، اختبارات 7 (5 ناجحة + 2 SkippableFact) |
| 2026-07-03 | ConPTY يتطلّب كونسول Windows حقيقي بالمضيف | إسناد الطفل للـ pseudoconsole يفشل تحت مضيف باثاته مُعاد توجيهها (git-bash / vstest testhost)؛ لا CREATE_NO_WINDOW/DETACHED/FreeConsole (تكسرها). الحلّ: اختبارا الإخراج SkippableFact + تُتحقَّق على كونسول حقيقي (WPF/CI) |
| 2026-07-03 | حلقة القراءة = خيط حاجب لا ReadAsync | الأنابيب المجهولة (CreatePipe) بلا overlapped I/O، فـ FileStream.ReadAsync لا يسلّم بموثوقية؛ خيط بقراءة حاجبة هو نمط Windows Terminal والمرجع |
| 2026-07-03 | تأجيل `SkiaSharp.Views.WPF` إلى T-005 | نسخة 3.116.1 بلا هدف net10-windows نظيف تجرّ OpenTK إطار .NET (NU1701)؛ تُضاف مع الرندرر لإبقاء بناء Phase 0 بلا تحذير |
| 2026-07-03 | **T-003 المحلّل منجَز (فصل صارم عن الـ buffer)** | `VtParser` بنيوي يبثّ إلى `IVtParserSink` (Print/Execute/Esc/Csi/Osc)؛ الدلالة (SGR) في `SgrProcessor`؛ تنفيذ CUP/ED/EL/DECSTBM/alt-screen على الشبكة يُنفَّذ في T-004 (الـ buffer يحقّق السِّنك). تغطية Vt=92.3% |
| 2026-07-03 | حلّ سباق git (فرع `terminal/engine-phase0`) | جلسة موازية commit‑ت T-001/T-002 على فرع مخصّص ثم بدّلت HEAD؛ لتفادي تعطيل الشجرة المشتركة عمِلتُ T-003 في **git worktree** معزول على نفس الفرع |
| 2026-07-03 | **T-004 الـ buffer منجَز (فضاء `Screen` منفصل عن `Vt`)** | `ScreenBuffer` يحقّق `IVtParserSink` وينفّذ دلالة الشبكة (CUP/ED/EL/DECSTBM/IL/DL/SU/SD/ICH/DCH + alt-screen)؛ خلية 8-بايت بأنماط مُدوّلة (`StyleTable`)؛ scrollback حلقة O(1) (ذاكرة ثابتة عند الحدّ)؛ dirty بـ `DirtyFromLine`. تغطية Screen=93.4% (65 اختبار). قرار: الخلية تخزّن `codepoint:int` (يدعم النطاق الفلكيّ/الإيموجي) لا `char` كالمرجع القديم |
| 2026-07-03 | scrollback يخزّن صفوف `Cell[]` خام (لا FrozenSpan) | يبسّط التحجيم بلا فقدان (الصفوف تُحفَظ كما هي)؛ التجميد إلى `StyledRun` يحدث لحظة `Snapshot()` فقط. reflow حقيقيّ للتاريخ عند التحجيم مؤجَّل لـ T-005 |
| 2026-07-04 | **مصالحة التتبّع: T-005 + T-006 كانا مُنجَزين والجدول متأخّراً** | بعد رجوع `Tools/Terminal/` واندماج المحرّك في `TerminalLauncher/`، تواصل عمل الرندرر/الإدخال دون تحديث الجدول. تحقّقتُ: بناء 0/0، اختبارات محرّك 127✅+2 skip، وكل معايير قبول T-005/T-006 محقَّقة كوداً (عدا التحقّق البصريّ = Phase 0 Exit Test على كونسول حقيقي، وligatures اللاتينية known-limitation). الثغرة الوحيدة المسدودة: **وميض المؤشّر** (`CursorBlinkOn` + `_blinkTimer` 530ms) |
| 2026-07-04 | T-005 وT-006 **مترابطان بالملف** (`TerminalTabView.xaml.cs`) لا مستقلَّان | الاستضافة (رندرر) والتقاط الإدخال في نفس الكنترول؛ فلا يُطلَق لهما أيجنتان متوازيان (خطر سباق git) — نُفِّذا/صولِحا في جلسة واحدة |
| 2026-07-05 | **مصالحة التتبّع: 8 تاسكات كانت مُنجَزة والجدول متأخّراً** (T-102/103/105/107 + T-201/203/205/207) | تُحقّق من الكود مباشرةً بعد أن أبلغ المستخدم بإكمالها: T-102 Tabs (`TerminalTabs` TabControl)، T-103 Split Panes (`SplitActivePane` قابل للتحجيم)، T-105 Search (شريط بحث + إبراز مطابقات)، T-107 ثيمات/خطوط (قوالب + إعدادات كامل الشاشة)، T-201 Command Palette (Ctrl+P فعليّ)، T-203 Quick Commands (شريط محفوظ)، T-205 Autocomplete (ghost text)، T-207 Command Blocks (OSC 133 + تنقّل). مؤكَّدة بكوميتات مدموجة (f56384f/b21e5c7/92b9503…). **الذكاء الاصطناعي (T-401…405) يبقى ⬜**: `AiAssistant` واجهة نظيفة لكن التنفيذ stub معطَّل بلا استدعاء API |
| 2026-07-04 | **T-101 + T-104 + T-110 عبر 3 أيجنتات متوازية في worktrees معزولة** | الثلاثة تلامس `TerminalTabView.xaml.cs` → عزلٌ في git worktree لكلٍّ (متفرّعة من `76bb378`)، ثمّ دمج تتابعيّ. التعارض الوحيد كان `Localization.cs` (مفاتيح T-101 vs T-104) حُلّ يدوياً؛ `MouseLeftDown` (T-104 نقر مزدوج vs T-110 حارس Ctrl) اندمج آلياً بترتيب صحيح. البناء 0/0، اختبارات 136✅+2 skip |
