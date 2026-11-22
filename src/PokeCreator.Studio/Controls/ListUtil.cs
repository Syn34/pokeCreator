using System.Windows.Forms;

namespace PokeCreator.Studio.Controls
{
    public static class ListUtil
    {
        /// <summary>
        /// Changes the text of one list entry without the list jumping or flashing.
        /// Windows removes and re-adds the entry, which drops and re-selects it (firing SelectedIndexChanged),
        /// so callers ignore that event while this runs.
        /// </summary>
        public static void Rename(ListBox lb, int i, object value)
        {
            if (lb == null || i < 0 || i >= lb.Items.Count) return;
            int top = lb.TopIndex; bool selected = lb.SelectedIndex == i;
            lb.BeginUpdate();
            try
            {
                lb.Items[i] = value;
                if (selected && lb.SelectedIndex != i) lb.SelectedIndex = i;
                if (top >= 0 && top < lb.Items.Count && lb.TopIndex != top) lb.TopIndex = top;
            }
            finally { lb.EndUpdate(); }
        }
    }
}
