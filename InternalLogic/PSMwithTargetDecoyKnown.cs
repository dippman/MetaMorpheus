﻿using Chemistry;
using MassSpectrometry;
using OldInternalLogic;
using Spectra;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InternalLogicEngineLayer
{
    public class PSMwithTargetDecoyKnown
    {

        #region Public Fields

        public ParentSpectrumMatch newPsm;

        #endregion Public Fields

        #region Public Constructors

        public PSMwithTargetDecoyKnown(ParentSpectrumMatch newPsm, HashSet<PeptideWithSetModifications> peptidesWithSetModifications, Tolerance fragmentTolerance, IMsDataFile<IMzSpectrum<MzPeak>> myMsDataFile)
        {
            this.newPsm = newPsm;
            IsDecoy = peptidesWithSetModifications.Count(b => b.Protein.IsDecoy) > 0;
            this.peptidesWithSetModifications = peptidesWithSetModifications;

            var allProductTypes = new List<ProductType> { ProductType.B, ProductType.Y };
            IMsDataScan<IMzSpectrum<MzPeak>> theScan;
            if (myMsDataFile != null && newPsm.matchedIonsList == null)
            {
                theScan = myMsDataFile.GetOneBasedScan(newPsm.scanNumber);

                var MatchedIonDict = new Dictionary<ProductType, double[]>();
                foreach (var huh in allProductTypes)
                {
                    var df = peptidesWithSetModifications.First().FastSortedProductMasses(new List<ProductType> { huh });
                    double[] matchedIonList = new double[df.Length];
                    MatchIons(theScan, fragmentTolerance, df, matchedIonList);
                    MatchedIonDict.Add(huh, matchedIonList);
                }

                newPsm.matchedIonsList = MatchedIonDict;
            }

            if (myMsDataFile != null && newPsm.LocalizedScores == null)
            {
                theScan = myMsDataFile.GetOneBasedScan(newPsm.scanNumber);
                var localizedScores = new List<double>();
                for (int indexToLocalize = 0; indexToLocalize < peptidesWithSetModifications.First().Length; indexToLocalize++)
                {
                    PeptideWithSetModifications localizedPeptide = peptidesWithSetModifications.First().Localize(indexToLocalize, ScanPrecursorMass - peptidesWithSetModifications.First().MonoisotopicMass);

                    var gg = localizedPeptide.FastSortedProductMasses(allProductTypes);
                    double[] matchedIonList = new double[gg.Length];
                    var score = MatchIons(theScan, fragmentTolerance, gg, matchedIonList);
                    localizedScores.Add(score);
                }
                newPsm.LocalizedScores = localizedScores;
            }

            PeptideMonoisotopicMass = peptidesWithSetModifications.First().MonoisotopicMass;
            FullSequence = peptidesWithSetModifications.First().Sequence;
            BaseSequence = peptidesWithSetModifications.First().BaseSequence;
            MissedCleavages = peptidesWithSetModifications.First().MissedCleavages;
            NumVariableMods = peptidesWithSetModifications.First().NumVariableMods;
        }

        #endregion Public Constructors

        #region Public Properties

        public HashSet<PeptideWithSetModifications> peptidesWithSetModifications { get; private set; }
        public bool IsDecoy { get; private set; }

        public double Score
        {
            get
            {
                return newPsm.score;
            }
        }

        public double ScanPrecursorMass
        {
            get
            {
                return newPsm.scanPrecursorMass;
            }
        }

        public List<double> LocalizedScores
        {
            get
            {
                return newPsm.LocalizedScores;
            }
        }

        public double PeptideMonoisotopicMass { get; private set; }

        public string FullSequence { get; private set; }

        public string BaseSequence { get; private set; }

        public int MissedCleavages { get; private set; }

        public int NumVariableMods { get; private set; }

        public string SequenceWithChemicalFormulas
        {
            get
            {
                return peptidesWithSetModifications.First().SequenceWithChemicalFormulas;
            }
        }

        #endregion Public Properties

        #region Internal Properties

        internal static string TabSeparatedHeader
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(ParentSpectrumMatch.GetTabSeparatedHeader() + '\t');
                sb.Append("Protein DB" + '\t');
                sb.Append("Protein Accession" + '\t');
                sb.Append("Protein FullName" + '\t');
                sb.Append("Peptide Description" + '\t');
                sb.Append("OneBasedStartResidueInProtein" + '\t');
                sb.Append("OneBasedEndResidueInProtein" + '\t');
                sb.Append("BaseSequence" + '\t');
                sb.Append("FullSequence" + '\t');
                sb.Append("numVariableMods" + '\t');
                sb.Append("MissedCleavages" + '\t');
                sb.Append("PeptideMonoisotopicMass" + '\t');
                sb.Append("MassDiff (Da)");
                return sb.ToString();
            }
        }

        #endregion Internal Properties

        #region Public Methods

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append(newPsm.ToString() + '\t');

            sb.Append(string.Join(" or ", peptidesWithSetModifications.Select(b => b.Protein.DatasetAbbreviation)) + "\t");
            sb.Append(string.Join(" or ", peptidesWithSetModifications.Select(b => b.Protein.Accession)) + "\t");
            sb.Append(string.Join(" or ", peptidesWithSetModifications.Select(b => b.Protein.FullName)) + "\t");
            sb.Append(string.Join(" or ", peptidesWithSetModifications.Select(b => b.PeptideDescription)) + "\t");
            sb.Append(string.Join(" or ", peptidesWithSetModifications.Select(b => b.OneBasedStartResidueInProtein)) + "\t");
            sb.Append(string.Join(" or ", peptidesWithSetModifications.Select(b => b.OneBasedEndResidueInProtein)) + "\t");
            sb.Append(BaseSequence.ToString(CultureInfo.InvariantCulture) + '\t');
            sb.Append(FullSequence.ToString(CultureInfo.InvariantCulture) + '\t');
            sb.Append(NumVariableMods.ToString(CultureInfo.InvariantCulture) + '\t');
            sb.Append(MissedCleavages.ToString(CultureInfo.InvariantCulture) + '\t');
            sb.Append(PeptideMonoisotopicMass.ToString("F5", CultureInfo.InvariantCulture) + '\t');
            sb.Append((ScanPrecursorMass - PeptideMonoisotopicMass).ToString("F5", CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        #endregion Public Methods

        #region Internal Methods

        internal static double MatchIons(IMsDataScan<IMzSpectrum<MzPeak>> thisScan, Tolerance product_mass_tolerance_value, double[] sorted_theoretical_product_masses_for_this_peptide, double[] matchedIonsList)
        {
            var TotalProductsHere = sorted_theoretical_product_masses_for_this_peptide.Length;
            if (TotalProductsHere == 0)
                return 0;
            int MatchingProductsHere = 0;
            double MatchingIntensityHere = 0;

            // speed optimizations
            double[] experimental_mzs = thisScan.MassSpectrum.xArray;
            double[] experimental_intensities = thisScan.MassSpectrum.yArray;
            int num_experimental_peaks = experimental_mzs.Length;

            int theoreticalIndex = 0;
            double nextTheoreticalMass = sorted_theoretical_product_masses_for_this_peptide[0];
            double nextTheoreticalMZ = nextTheoreticalMass + Constants.ProtonMass;

            double currentExperimentalMZ;
            for (int i = 0; i < num_experimental_peaks; i++)
            {
                currentExperimentalMZ = experimental_mzs[i];
                if (product_mass_tolerance_value.Within(currentExperimentalMZ, nextTheoreticalMZ))
                {
                    MatchingProductsHere++;
                    MatchingIntensityHere += experimental_intensities[i];
                    matchedIonsList[theoreticalIndex] = nextTheoreticalMass;
                }
                else if (currentExperimentalMZ < nextTheoreticalMZ)
                    continue;
                else
                    matchedIonsList[theoreticalIndex] = -nextTheoreticalMass;
                i--;
                // Passed a theoretical! Move counter forward
                theoreticalIndex++;
                if (theoreticalIndex == TotalProductsHere)
                    break;
                nextTheoreticalMass = sorted_theoretical_product_masses_for_this_peptide[theoreticalIndex];
                nextTheoreticalMZ = nextTheoreticalMass + Constants.ProtonMass;
            }
            return MatchingProductsHere + MatchingIntensityHere / thisScan.TotalIonCurrent;
        }

        #endregion Internal Methods

    }
}