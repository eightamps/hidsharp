﻿#region License
/* Copyright 2011 James F. Bellinger <http://www.zer7.com>

   Permission to use, copy, modify, and/or distribute this software for any
   purpose with or without fee is hereby granted, provided that the above
   copyright notice and this permission notice appear in all copies.

   THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
   WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
   MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
   ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
   WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
   ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
   OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE. */
#endregion

using System;
using System.Collections.Generic;

namespace HidSharp.ReportDescriptors.Parser
{
    public class ReportDescriptorParser
    {
        public ReportDescriptorParser()
        {
            RootCollection = new ReportCollection();
            GlobalItemStateStack = new List<IDictionary<GlobalItemTag, EncodedItem>>();
            LocalItemState = new List<KeyValuePair<LocalItemTag, uint>>();
            Reports = new List<Report>();
            Clear();
        }

        public void Clear()
        {
            CurrentCollection = RootCollection;
            RootCollection.Clear();

            GlobalItemStateStack.Clear();
            GlobalItemStateStack.Add(new Dictionary<GlobalItemTag, EncodedItem>());
            LocalItemState.Clear();
            Reports.Clear();
            ReportsUseID = false;
        }

        public EncodedItem GetGlobalItem(GlobalItemTag tag)
        {
            EncodedItem value;
            GlobalItemState.TryGetValue(tag, out value);
            return value;
        }

        public uint GetGlobalItemValue(GlobalItemTag tag)
        {
            EncodedItem item = GetGlobalItem(tag);
            return item != null ? item.DataValue : 0;
        }

        public Report GetReport(ReportType type, byte id)
        {
            Report report;
            if (!TryGetReport(type, id, out report)) { throw new ArgumentException("Report not found."); }
            return report;
        }

        public bool TryGetReport(ReportType type, byte id, out Report report)
        {
            for (int i = 0; i < Reports.Count; i++)
            {
                report = Reports[i];
                if (report.Type == type && report.ID == id) { return true; }
            }

            report = null; return false;
        }

        public bool IsGlobalItemSet(GlobalItemTag tag)
        {
            return GlobalItemState.ContainsKey(tag);
        }

        public void Parse(IEnumerable<EncodedItem> items)
        {
            foreach (EncodedItem item in items) { Parse(item); }
        }

        public void Parse(EncodedItem item)
        {
            uint value = item.DataValue;

            switch (item.Type)
            {
                case ItemType.Main:
                    ParseMain(item.TagForMain, value);
                    LocalItemState.Clear();
                    break;

                case ItemType.Local:
                    switch (item.TagForLocal)
                    {
                        case LocalItemTag.Usage:
                        case LocalItemTag.UsageMinimum:
                        case LocalItemTag.UsageMaximum:
                            if (value <= 0xffff) { value |= GetGlobalItemValue(GlobalItemTag.UsagePage) << 16; }
                            break;
                    }
                    LocalItemState.Add(new KeyValuePair<LocalItemTag, uint>(item.TagForLocal, value));
                    break;

                case ItemType.Global:
                    switch (item.TagForGlobal)
                    {
                        case GlobalItemTag.Push:
                            GlobalItemStateStack.Add(new Dictionary<GlobalItemTag, EncodedItem>(GlobalItemState));
                            break;

                        case GlobalItemTag.Pop:
                            GlobalItemStateStack.RemoveAt(GlobalItemState.Count - 1);
                            break;

                        default:
                            switch (item.TagForGlobal)
                            {
                                case GlobalItemTag.ReportID:
                                    ReportsUseID = true; break;
                            }

                            GlobalItemState[item.TagForGlobal] = item;
                            break;
                    }
                    break;
            }
        }

        void ParseMain(MainItemTag tag, uint value)
        {
            LocalIndexes indexes = null;

            switch (tag)
            {
                case MainItemTag.Collection:
                    ReportCollection collection = new ReportCollection();
                    collection.Parent = CurrentCollection;
                    collection.Type = (CollectionType)value;
                    CurrentCollection = collection;
                    indexes = collection.Indexes; break;

                case MainItemTag.EndCollection:
                    CurrentCollection = CurrentCollection.Parent; break;

                case MainItemTag.Input:
                case MainItemTag.Output:
                case MainItemTag.Feature:
                    ParseDataMain(tag, value, out indexes); break;
            }

            if (indexes != null) { ParseMainIndexes(indexes); }
        }

        void AddIndex(List<KeyValuePair<int, uint>> list, int action, uint value)
        {
            list.Add(new KeyValuePair<int, uint>(action, value));
        }

        void UpdateIndexMinimum(ref IndexBase index, uint value)
        {
            if (!(index is IndexRange)) { index = new IndexRange(); }
            ((IndexRange)index).Minimum = value;
        }

        void UpdateIndexMaximum(ref IndexBase index, uint value)
        {
            if (!(index is IndexRange)) { index = new IndexRange(); }
            ((IndexRange)index).Maximum = value;
        }

        void UpdateIndexList(List<uint> values, int delimiter,
                             ref IndexBase index, uint value)
        {
            values.Add(value);
            UpdateIndexListCommit(values, delimiter, ref index);
        }

        void UpdateIndexListCommit(List<uint> values, int delimiter,
                                   ref IndexBase index)
        {
            if (delimiter != 0 || values.Count == 0) { return; }
            if (!(index is IndexList)) { index = new IndexList(); }
            ((IndexList)index).Indices.Add(new List<uint>(values));
            values.Clear();
        }

        void ParseMainIndexes(LocalIndexes indexes)
        {
            int delimiter = 0;
            List<uint> designatorValues = new List<uint>(); IndexBase designator = IndexBase.Unset;
            List<uint> stringValues = new List<uint>(); IndexBase @string = IndexBase.Unset;
            List<uint> usageValues = new List<uint>(); IndexBase usage = IndexBase.Unset;

            foreach (KeyValuePair<LocalItemTag, uint> kvp in LocalItemState)
            {
                switch (kvp.Key)
                {
                    case LocalItemTag.DesignatorMinimum: UpdateIndexMinimum(ref designator, kvp.Value); break;
                    case LocalItemTag.StringMinimum: UpdateIndexMinimum(ref @string, kvp.Value); break;
                    case LocalItemTag.UsageMinimum: UpdateIndexMinimum(ref usage, kvp.Value); break;

                    case LocalItemTag.DesignatorMaximum: UpdateIndexMaximum(ref designator, kvp.Value); break;
                    case LocalItemTag.StringMaximum: UpdateIndexMaximum(ref @string, kvp.Value); break;
                    case LocalItemTag.UsageMaximum: UpdateIndexMaximum(ref usage, kvp.Value); break;

                    case LocalItemTag.DesignatorIndex: UpdateIndexList(designatorValues, delimiter, ref designator, kvp.Value); break;
                    case LocalItemTag.StringIndex: UpdateIndexList(stringValues, delimiter, ref @string, kvp.Value); break;
                    case LocalItemTag.Usage: UpdateIndexList(usageValues, delimiter, ref usage, kvp.Value); break;

                    case LocalItemTag.Delimiter:
                        if (kvp.Value == 1)
                        {
                            if (delimiter++ == 0)
                            {
                                designatorValues.Clear();
                                stringValues.Clear();
                                usageValues.Clear();
                            }
                        }
                        else if (kvp.Value == 0)
                        {
                            delimiter--;
                            UpdateIndexListCommit(designatorValues, delimiter, ref designator);
                            UpdateIndexListCommit(stringValues, delimiter, ref @string);
                            UpdateIndexListCommit(usageValues, delimiter, ref usage);
                        }
                        break;
                }
            }

            indexes.Designator = designator;
            indexes.String = @string;
            indexes.Usage = usage;
        }

        void ParseDataMain(MainItemTag tag, uint value, out LocalIndexes indexes)
        {
            ReportSegment segment = new ReportSegment();
            segment.Flags = (DataMainItemFlags)value;
            segment.Parent = CurrentCollection;
            segment.ElementCount = (int)GetGlobalItemValue(GlobalItemTag.ReportCount);
            segment.ElementSize = (int)GetGlobalItemValue(GlobalItemTag.ReportSize);
            segment.Unit = new Units.Unit(GetGlobalItemValue(GlobalItemTag.Unit));
            segment.UnitExponent = Units.Unit.DecodeExponent(GetGlobalItemValue(GlobalItemTag.UnitExponent));
            indexes = segment.Indexes;

            EncodedItem logicalMinItem = GetGlobalItem(GlobalItemTag.LogicalMinimum);
            EncodedItem logicalMaxItem = GetGlobalItem(GlobalItemTag.LogicalMaximum);
            segment.LogicalIsSigned =
                (logicalMinItem != null && logicalMinItem.DataValueMayBeNegative) ||
                (logicalMaxItem != null && logicalMaxItem.DataValueMayBeNegative);
            int logicalMinimum = logicalMinItem == null ? 0 : segment.LogicalIsSigned ? logicalMinItem.DataValueSigned : (int)logicalMinItem.DataValue;
            int logicalMaximum = logicalMaxItem == null ? 0 : segment.LogicalIsSigned ? logicalMaxItem.DataValueSigned : (int)logicalMaxItem.DataValue;
            int physicalMinimum = (int)GetGlobalItemValue(GlobalItemTag.PhysicalMinimum);
            int physicalMaximum = (int)GetGlobalItemValue(GlobalItemTag.PhysicalMaximum);
            if (!IsGlobalItemSet(GlobalItemTag.PhysicalMinimum) ||
                !IsGlobalItemSet(GlobalItemTag.PhysicalMaximum) ||
                (physicalMinimum == 0 && physicalMaximum == 0))
            {
                physicalMinimum = logicalMinimum; physicalMaximum = logicalMaximum;
            }

            segment.LogicalMinimum = logicalMinimum; segment.LogicalMaximum = logicalMaximum;
            segment.PhysicalMinimum = physicalMinimum; segment.PhysicalMaximum = physicalMaximum;

            Report report;
            ReportType reportType
                = tag == MainItemTag.Output ? ReportType.Output
                : tag == MainItemTag.Feature ? ReportType.Feature
                : ReportType.Input;
            uint reportID = GetGlobalItemValue(GlobalItemTag.ReportID);
            if (!TryGetReport(reportType, (byte)reportID, out report))
            {
                report = new Report() { ID = (byte)reportID, Type = reportType };
                Reports.Add(report);
            }
            segment.Report = report;
        }

        public ReportCollection CurrentCollection
        {
            get;
            set;
        }

        public ReportCollection RootCollection
        {
            get;
            private set;
        }

        public IDictionary<GlobalItemTag, EncodedItem> GlobalItemState
        {
            get { return GlobalItemStateStack[GlobalItemStateStack.Count - 1]; }
        }

        public IList<IDictionary<GlobalItemTag, EncodedItem>> GlobalItemStateStack
        {
            get;
            private set;
        }

        public IList<KeyValuePair<LocalItemTag, uint>> LocalItemState
        {
            get;
            private set;
        }

        IEnumerable<Report> FilterReports(ReportType reportType)
        {
            foreach (Report report in Reports)
            {
                if (report.Type == reportType) { yield return report; }
            }
        }

        public IEnumerable<Report> InputReports
        {
            get { return FilterReports(ReportType.Input); }
        }

        public int InputReportMaxLength
        {
            get
            {
                int length = 0;
                foreach (Report report in InputReports) { length = Math.Max(length, report.Length); }
                return length;
            }
        }

        public IEnumerable<Report> OutputReports
        {
            get { return FilterReports(ReportType.Output); }
        }

        public IEnumerable<Report> FeatureReports
        {
            get { return FilterReports(ReportType.Feature); }
        }

        public IList<Report> Reports
        {
            get;
            private set;
        }

        public bool ReportsUseID
        {
            get;
            set;
        }

        public IEnumerable<IEnumerable<uint>> InputUsages
        {
            get
            {
                foreach (Report report in InputReports)
                {
                    foreach (ReportSegment segment in report.Segments)
                    {
                        IndexBase usages = segment.Indexes.Usage;
                        for (int i = 0; i < usages.Count; i++)
                        {
                            yield return usages.ValuesFromIndex(i);
                        }
                    }
                }
            }
        }
    }
}