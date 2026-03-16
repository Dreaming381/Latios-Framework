using System.Collections.Generic;
using Unity.Collections;

namespace Latios.Calci.Clipper2
{
    internal struct HorzSegment
    {
        public int  leftOp;
        public int  rightOp;
        public bool leftToRight;
        public HorzSegment(int op)
        {
            leftOp      = op;
            rightOp     = -1;
            leftToRight = true;
        }
    };
    internal struct HorzSegSorter : IComparer<HorzSegment>
    {
        NativeList<OutPt> m_outPtList;
        public HorzSegSorter (NativeList<OutPt> outPtList)
        {
            m_outPtList = outPtList;
        }
        public int Compare(HorzSegment hs1, HorzSegment hs2)
        {
            if (hs1.rightOp == -1)
            {
                return hs2.rightOp == -1 ? 0 : 1;
            }
            else if (hs2.rightOp == -1)
                return -1;
            else
                return m_outPtList[hs1.leftOp].pt.x.CompareTo(m_outPtList[hs2.leftOp].pt.x);
        }
    }
}  //namespace

