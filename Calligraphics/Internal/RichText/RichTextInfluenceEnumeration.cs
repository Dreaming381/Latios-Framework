using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calligraphics.RichText
{
    internal struct RichTextInfluenceCharEnumerator : IEnumerator<RichTextInfluenceContext>
    {
        NativeList<RichTextTag> m_tags;
        CalliString.Enumerator  m_stringEnumerator;
        int                     m_firstActiveTagIndex;
        int                     m_lastActiveTagIndex;
        int                     m_currentCharIndex;
        int                     m_skipCharsUntilIndex;
        int                     m_nextTagTerminationOfConcernCharIndex;

        public RichTextInfluenceCharEnumerator(NativeList<RichTextTag> tags, CalliString calliString)
        {
            m_tags                                 = tags;
            m_stringEnumerator                     = calliString.GetEnumerator();
            m_currentCharIndex                     = -1;
            m_firstActiveTagIndex                  = 0;
            m_lastActiveTagIndex                   = -1;
            m_skipCharsUntilIndex                  = 0;
            m_nextTagTerminationOfConcernCharIndex = int.MaxValue;
        }

        public RichTextInfluenceContext Current => new RichTextInfluenceContext(m_tags.AsArray().GetSubArray(m_firstActiveTagIndex,
                                                                                                             m_lastActiveTagIndex - m_firstActiveTagIndex + 1),
                                                                                m_stringEnumerator.Current);

        object IEnumerator.Current => Current;

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            while (true)
            {
                if (!m_stringEnumerator.MoveNext())
                    return false;
                m_currentCharIndex++;

                if (m_currentCharIndex < m_skipCharsUntilIndex)
                    continue;

                // Check for new tag
                if (m_lastActiveTagIndex + 1 < m_tags.Length && m_tags.ElementAt(m_lastActiveTagIndex + 1).startTagStartIndex <= m_currentCharIndex)
                {
                    m_lastActiveTagIndex++;
                    ref var tag                            = ref m_tags.ElementAt(m_lastActiveTagIndex);
                    m_skipCharsUntilIndex                  = tag.scopeStartIndex;
                    m_nextTagTerminationOfConcernCharIndex = math.min(m_nextTagTerminationOfConcernCharIndex, tag.endTagStartIndex);
                    continue;
                }

                // Check for invalidation of existing tags
                if (m_currentCharIndex >= m_nextTagTerminationOfConcernCharIndex)
                {
                    m_nextTagTerminationOfConcernCharIndex = int.MaxValue;
                    int previousTag                        = m_firstActiveTagIndex;
                    for (int i = m_firstActiveTagIndex; i <= m_lastActiveTagIndex;)
                    {
                        ref var tag = ref m_tags.ElementAt(i);
                        if (tag.endTagStartIndex <= m_currentCharIndex)
                        {
                            // Remove the tag from the list
                            if (i == m_firstActiveTagIndex)
                                m_firstActiveTagIndex += tag.nextInfluenceTagOffset;
                            else
                                m_tags.ElementAt(previousTag).nextInfluenceTagOffset += tag.nextInfluenceTagOffset;

                            m_skipCharsUntilIndex = tag.endTagStartIndex + tag.endTagOffset + 1;
                        }
                        else
                            m_nextTagTerminationOfConcernCharIndex = math.min(m_nextTagTerminationOfConcernCharIndex, tag.endTagStartIndex);

                        i += tag.nextInfluenceTagOffset;
                    }

                    if (m_currentCharIndex < m_skipCharsUntilIndex)
                        continue;
                }

                return true;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < m_tags.Length; i++)
                m_tags.ElementAt(i).nextInfluenceTagOffset = 1;
            m_stringEnumerator.Reset();
            m_currentCharIndex                     = -1;
            m_firstActiveTagIndex                  = 0;
            m_lastActiveTagIndex                   = -1;
            m_skipCharsUntilIndex                  = 0;
            m_nextTagTerminationOfConcernCharIndex = int.MaxValue;
        }
    }

    internal struct RichTextInfluenceContext
    {
        NativeArray<RichTextTag> m_tags;
        Unicode.Rune             m_currentChar;

        public RichTextInfluenceContext(NativeArray<RichTextTag> tags, Unicode.Rune currentChar)
        {
            m_tags        = tags;
            m_currentChar = currentChar;
        }

        public Unicode.Rune character => m_currentChar;

        public Enumerator GetEnumerator() => new Enumerator(m_tags);

        public struct Enumerator : IEnumerator<RichTextTag>
        {
            NativeArray<RichTextTag> m_tags;
            int                      m_currentTagIndex;

            public Enumerator(NativeArray<RichTextTag> tags)
            {
                m_tags            = tags;
                m_currentTagIndex = -1;
            }

            public RichTextTag Current => m_tags[m_currentTagIndex];

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (m_currentTagIndex < 0)
                {
                    m_currentTagIndex++;
                    return m_tags.Length > 0;
                }

                if (m_currentTagIndex >= m_tags.Length)
                    return false;
                m_currentTagIndex += m_tags[m_currentTagIndex].nextInfluenceTagOffset;
                return m_currentTagIndex < m_tags.Length;
            }

            public void Reset()
            {
                m_currentTagIndex = 0;
            }
        }
    }
}

