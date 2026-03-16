namespace Latios.Calci.Clipper2
{
    internal struct TreeNode
    {
        public int outrecIdx;
        public int parent;
        public int firstChild;
        public int nextSibling;

        public int childCount;
        public TreeNode(bool dummy)
        {
            this.outrecIdx   = -1;
            this.parent      = -1;
            this.nextSibling = -1;
            this.firstChild  = -1;
            this.childCount  = 0;
        }
    };
}  //namespace

