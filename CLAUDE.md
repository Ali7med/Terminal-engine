# TerminalLauncher — تعليمات المشروع

أداة WPF (`net10.0-windows`) داخل الحلّ: تيرمنال مدمج + مشغّل أوامر محفوظة.

## قاعدة الإصدار ولوحة «ما الجديد» (إلزامية — الدستور)

**مع كلّ موجة تغيير مكتملة، يجب:**

1. **رفع `AppVersion.Current`** في
   [`Services/AppVersion.cs`](Services/AppVersion.cs)
   — المصدر الوحيد لرقم الإصدار (Single Source of Truth). القاعدة الافتراضية: **ارفع MINOR وصفّر PATCH**
   (`x.y.z` → `x.(y+1).0`). استعمل MAJOR فقط لتغيير جذري/كسر توافق، وPATCH لإصلاح صغير ضمن الموجة نفسها.
   حدّث أيضاً `AppVersion.ReleasedDate` بتاريخ اليوم (ISO: `YYYY-MM-DD`).

2. **إضافة مدخلة مطابقة في [`CHANGELOG.md`](CHANGELOG.md)** (جذر المشروع) بالبنية:
   ```
   ## [x.y.z] - YYYY-MM-DD
   ### HIGHLIGHTS
   - ...
   ### NEW
   - ...
   ### IMPROVED
   - ...
   ### FIXED
   - ...
   ```
   الأقسام كلّها اختيارية (احذف الفارغ). البنية **machine-parseable** — لا تغيّر أنماط
   `## [ver] - date` و`### SECTION` و`- bullet`. البنود تُعرض حرفياً.

**لماذا:** لوحة «ما الجديد» ([`Views/WhatsNewWindow.xaml`](Views/WhatsNewWindow.xaml)) تُحلّل `CHANGELOG.md`
وقت التشغيل (مورد مُدمَج عبر LogicalName `TerminalLauncher.CHANGELOG.md`، مع احتياط قراءته من القرص أثناء
التطوير) وتعرض تاريخ الإصدارات كاملاً. تظهر تلقائياً مرّة واحدة عند تغيّر الإصدار — تُخزَّن آخر نسخة عُرضت
في `AppSettings.LastWhatsNewVersion` — ويمكن فتحها يدوياً عبر زرّ «ما الجديد» في شريط العنوان
(`WhatsNewWindow.ShowManual`).

**ممنوع:** كتابة رقم إصدار ثابت في XAML أو code-behind — اقرأه دائماً من `AppVersion.Current`.

## اصطلاحات الواجهة (تذكير)

- كلّ نصّ مترجَم عبر `Loc.T("key")` مع مفتاح في [`Services/Localization.cs`](Services/Localization.cs) (عربي/إنجليزي).
- الاتجاه (RTL/LTR) يتبع اللغة حيّاً عبر `Loc.Flow` / حدث `Loc.Changed`.
- الأرقام لاتينية دائماً: `NumberSubstitution.Substitution="European"`.
- الثيم عبر `{DynamicResource Brush.*}` (فاتح/داكن + لكنة) عبر `ThemeManager`.
