﻿using System.Collections.Generic;
using System.Text;

namespace InternalLogicEngineLayer
{
    public class IndexResults : MyResults
    {
        #region Public Constructors

        public IndexResults(List<CompactPeptide> peptideIndex, Dictionary<float, List<int>> fragmentIndexDict, IndexEngine indexParams) : base(indexParams)
        {
            this.PeptideIndex = peptideIndex;
            this.FragmentIndexDict = fragmentIndexDict;
        }

        #endregion Public Constructors

        #region Public Properties

        public Dictionary<float, List<int>> FragmentIndexDict { get; private set; }
        public List<CompactPeptide> PeptideIndex { get; private set; }

        #endregion Public Properties

        #region Protected Properties

        protected override string StringForOutput
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("\t\tfragmentIndexDict.Count: " + FragmentIndexDict.Count);
                sb.Append("\t\tpeptideIndex.Count: " + PeptideIndex.Count);
                return sb.ToString();
            }
        }

        #endregion Protected Properties
    }
}