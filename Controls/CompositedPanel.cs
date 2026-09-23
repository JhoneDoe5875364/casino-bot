using System.Windows.Forms;

namespace PragmaticBot.Controls;

/// <summary>
/// Scrolling container that composites its whole child tree before painting.
/// <para>
/// A plain <see cref="Panel"/> with AutoScroll scrolls by blitting the existing pixels and then
/// invalidating only the newly exposed strip. Child controls that repaint a beat later leave their
/// old pixels behind at the scrolled offset — the ghost text seen over the room titles. Plain
/// double buffering does not help, because each child label paints into its own device context;
/// WS_EX_COMPOSITED is what makes the parent buffer the entire hierarchy.
/// </para>
/// </summary>
public sealed class CompositedPanel : Panel
{
	private const int WS_EX_COMPOSITED = 0x02000000;

	public CompositedPanel()
	{
		DoubleBuffered = true;
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams cp = base.CreateParams;
			cp.ExStyle |= WS_EX_COMPOSITED;
			return cp;
		}
	}
}

/// <summary>Flow layout counterpart of <see cref="CompositedPanel"/>; see there for why.</summary>
public sealed class CompositedFlowLayoutPanel : FlowLayoutPanel
{
	private const int WS_EX_COMPOSITED = 0x02000000;

	public CompositedFlowLayoutPanel()
	{
		DoubleBuffered = true;
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams cp = base.CreateParams;
			cp.ExStyle |= WS_EX_COMPOSITED;
			return cp;
		}
	}
}
