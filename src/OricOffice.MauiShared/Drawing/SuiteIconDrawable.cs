using Microsoft.Maui.Graphics;
using OricOffice.Core.Ui;

namespace OricOffice.MauiShared.Drawing;

/// <summary>
/// Draws the toolbar icons as vector line art with ICanvas — crisp at any density,
/// consistent across platforms, and independent of any font.
/// </summary>
public sealed class SuiteIconDrawable : IDrawable
{
    public SuiteIcon Icon { get; }

    public Color Stroke { get; set; } = Theme.SuiteTheme.IconStroke;

    public SuiteIconDrawable(SuiteIcon icon) => Icon = icon;

    public void Draw(ICanvas canvas, RectF bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        canvas.StrokeColor = Stroke;
        canvas.FillColor = Stroke;
        canvas.StrokeSize = 1.6f;

        // Normalise to a 24×24 design grid centred in the bounds.
        float s = Math.Min(bounds.Width, bounds.Height);
        float ox = bounds.X + (bounds.Width - s) / 2f;
        float oy = bounds.Y + (bounds.Height - s) / 2f;
        float U(float u) => ox + u * s / 24f;
        float V(float v) => oy + v * s / 24f;

        void Line(float x1, float y1, float x2, float y2)
            => canvas.DrawLine(U(x1), V(y1), U(x2), V(y2));

        void Rect(float x, float y, float w, float h, float r)
            => canvas.DrawRoundedRectangle(U(x), V(y), U(w) - U(0), V(h) - V(0), r * s / 24f);

        switch (Icon)
        {
            case SuiteIcon.New:
                Rect(4, 3, 16, 18, 1.5f);
                Line(7, 9, 17, 9);
                Line(7, 12.5f, 17, 12.5f);
                Line(7, 16, 14, 16);
                break;

            case SuiteIcon.Open:
                Rect(3, 7, 18, 13, 1.5f);
                Line(3, 7, 3, 5.5f);
                Rect(4, 3.5f, 7, 3.5f, 1f);
                break;

            case SuiteIcon.Save:
                Rect(3, 3, 18, 18, 2f);
                Rect(8, 13, 8, 6, 1f);
                Rect(13.5f, 4.5f, 5, 6, 0.5f);
                break;

            case SuiteIcon.SaveAs:
                Rect(2, 3, 15, 15, 1.5f);
                Rect(7, 11, 6, 4.5f, 0.5f);
                Line(18, 19, 23, 19);
                Line(20.5f, 16.5f, 20.5f, 21.5f);
                break;

            case SuiteIcon.Cut:
                canvas.DrawEllipse(U(4), V(15.5f), U(6) - U(0), V(6) - V(0));
                canvas.DrawEllipse(U(14), V(15.5f), U(6) - U(0), V(6) - V(0));
                Line(6.5f, 15, 18, 4);
                Line(17.5f, 15, 6, 4);
                break;

            case SuiteIcon.Copy:
                Rect(7, 3, 13, 14, 1.5f);
                Rect(4, 7, 13, 14, 1.5f);
                break;

            case SuiteIcon.Paste:
                Rect(5, 4.5f, 14, 16.5f, 2f);
                Rect(9, 2.5f, 6, 4, 1f);
                Line(8, 11, 16, 11);
                Line(8, 14.5f, 16, 14.5f);
                Line(8, 18, 13, 18);
                break;

            case SuiteIcon.Sum:
                canvas.FontSize = s * 0.8f;
                canvas.DrawString("Σ", bounds, HorizontalAlignment.Center,
                    VerticalAlignment.Center, TextFlow.ClipBounds);
                break;

            case SuiteIcon.Clear:
                Line(6, 6, 18, 18);
                Line(18, 6, 6, 18);
                break;

            case SuiteIcon.Trash:
                Line(4, 6.5f, 20, 6.5f);
                Line(9.5f, 4, 14.5f, 4);
                Line(5.5f, 7, 6.75f, 21);
                Line(18.5f, 7, 17.25f, 21);
                Line(6.75f, 21, 17.25f, 21);
                Line(10, 10, 10.5f, 18);
                Line(14, 10, 13.5f, 18);
                break;

            case SuiteIcon.AddRow:
                Rect(3, 8.5f, 18, 7, 1.5f);
                Line(12, 10.5f, 12, 13.5f);
                Line(10.5f, 12, 13.5f, 12);
                break;

            case SuiteIcon.AddColumn:
                Rect(8.5f, 3, 7, 18, 1.5f);
                Line(10.5f, 12, 13.5f, 12);
                Line(12, 10.5f, 12, 13.5f);
                break;

            case SuiteIcon.AddField:
                Rect(4, 4, 16, 16, 3f);
                Line(12, 8, 12, 16);
                Line(8, 12, 16, 12);
                break;

            case SuiteIcon.Field:
                Rect(9, 3, 6, 18, 1f);
                break;

            case SuiteIcon.SortAsc:
                Line(5, 7, 10, 7);
                Line(5, 12, 15, 12);
                Line(5, 17, 20, 17);
                break;

            case SuiteIcon.SortDesc:
                Line(5, 7, 20, 7);
                Line(5, 12, 15, 12);
                Line(5, 17, 10, 17);
                break;

            case SuiteIcon.Filter:
                Line(3, 5, 21, 5);
                Line(4.5f, 5, 10, 12.5f);
                Line(19.5f, 5, 14, 12.5f);
                Line(12, 13, 12, 19.5f);
                break;

            case SuiteIcon.Check:
                Line(5, 13, 10, 18);
                Line(10, 18, 19, 7);
                break;

            case SuiteIcon.About:
                canvas.DrawEllipse(U(3), V(3), U(18) - U(0), V(18) - V(0));
                canvas.FontSize = s * 0.75f;
                canvas.DrawString("i", bounds, HorizontalAlignment.Center,
                    VerticalAlignment.Center, TextFlow.ClipBounds);
                break;

            case SuiteIcon.ZoomIn:
                canvas.FontSize = s * 0.62f;
                canvas.DrawString("A+", bounds, HorizontalAlignment.Center,
                    VerticalAlignment.Center, TextFlow.ClipBounds);
                break;

            case SuiteIcon.ZoomOut:
                canvas.FontSize = s * 0.62f;
                canvas.DrawString("A-", bounds, HorizontalAlignment.Center,
                    VerticalAlignment.Center, TextFlow.ClipBounds);
                break;

            case SuiteIcon.Record:
                Rect(3, 5, 18, 14, 2f);
                canvas.DrawEllipse(U(6), V(9.5f), U(4) - U(0), V(4) - V(0));
                Line(12, 11, 18, 11);
                Line(12, 14.5f, 16, 14.5f);
                break;

            default:
                // None: draw nothing.
                break;
        }
    }
}
