using System;
using TerminalLauncher.Terminal;

namespace TerminalLauncher.Controls;

/// <summary>
/// واجهة عرض تيرمنال محايدة تقنياً (لا تعرف SkiaSharp/Direct2D): تفصل «ماذا نرسم» عن «كيف نرسمه».
/// تُوثّق الحدّ (seam) بحيث يمكن استبدال الخلفية (Skia ← Direct2D أو غيرها) لاحقاً دون تغيير المستهلك.
/// </summary>
public interface IRenderer
{
    /// <summary>يُسلّم لقطة شاشة جديدة للعرض؛ يستدعي إعادة الرسم عند الحاجة.</summary>
    void SetSnapshot(ScreenSnapshot snapshot);

    /// <summary>
    /// يحسب عدد الخلايا (أعمدة×صفوف) التي تتّسع في مساحة بالبكسل حسب مقاييس الخطّ الحاليّة.
    /// تستعمله طبقة الواجهة لإبلاغ <c>TerminalScreen.SetSize</c> بالأبعاد المناسبة.
    /// </summary>
    (int Cols, int Rows) Measure(double pixelWidth, double pixelHeight);

    /// <summary>يُطلَق حين يلزم إعادة رسم (مثلاً عند تغيّر الحجم) لتُجدوِل طبقة الواجهة الرسم.</summary>
    event Action? RenderNeeded;
}
