﻿/*
  QualityHighlighter plugin for KeePass 2.x.
  Copyright (C) 2016 by Scott Richter <scott.d.richter@gmail.com>

  Modified by jaege <jaege8@gmail.com>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using KeePass.Ecas;
using KeePass.Plugins;
using KeePass.UI;
using KeePass.Util.Spr;
using KeePassLib;
using KeePassLib.Cryptography;
using KeePassLib.Security;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace QualityHighlighter
{
    /// <summary>
    /// A simple plugin to highlight rows in the KeePass database based on password quality.
    /// Allows for easily spotting weak passwords.
    /// </summary>
    public class QualityHighlighterExt : Plugin
    {
        /// <summary>
        /// Name of the custom toolbar button to toggle the highlights.
        /// </summary>
        private const string ToggleBtnCommand = "QualityHighlighterToggle";

        /// <summary>
        /// Name of the config setting for this plugin.
        /// </summary>
        private const string CustomConfigName = "QHL_HighlightsOn";

        private IPluginHost _host = null;
        private bool _highlightsOn = true;

        //Quality classification cutoffs, populated per KeePass website.
        //In the future, might make these configurable.
        private SortedList<uint, Color> QualityDelimiter = new SortedList<uint, Color> {
            {             0, Color.FromArgb(unchecked((int)0xFFFFFFFF)) },
            {            64, Color.FromArgb(unchecked((int)0xFFFF0000)) },
            {            80, Color.FromArgb(unchecked((int)0xFFFF9933)) },
            {           112, Color.FromArgb(unchecked((int)0xFFFFFF66)) },
            {           128, Color.FromArgb(unchecked((int)0xFFCCFF99)) },
            { uint.MaxValue, Color.FromArgb(unchecked((int)0xFFCCFFCC)) },
        };

        public override bool Initialize(IPluginHost host)
        {
            _highlightsOn = true;

            _host = host;
            if (_host == null) { Debug.Assert(false); }

            _host.MainWindow.UIStateUpdated += MainWindow_UIStateUpdated;

            _host.MainWindow.AddCustomToolBarButton(ToggleBtnCommand, "Toggle Highlights", "Toggle quality level highlights on or off.");
            _host.TriggerSystem.RaisingEvent += TriggerSystem_RaisingEvent;

            _highlightsOn = _host.CustomConfig.GetBool(CustomConfigName, true);

            return true;
        }

        public override void Terminate()
        {
            _host.MainWindow.UIStateUpdated -= MainWindow_UIStateUpdated;
            _host.TriggerSystem.RaisingEvent -= TriggerSystem_RaisingEvent;

            _host.MainWindow.RemoveCustomToolBarButton(ToggleBtnCommand);

            _host.CustomConfig.SetBool(CustomConfigName, _highlightsOn);
        }

        public override string UpdateUrl
        {
            get
            {
                return "https://cdn.rawgit.com/sdrichter/QualityHighlighter/master/VERSION";
            }
        }
        
        private void TriggerSystem_RaisingEvent(object sender, EcasRaisingEventArgs e)
        {
            //Check if the event is our toggle button and toggle the highlights on or off accordingly.
            EcasPropertyDictionary dict = e.Properties;
            if(dict == null) { Debug.Assert(false); return; }

            string command = e.Properties.Get<string>(EcasProperty.CommandID);

            if (command != null && command.Equals(ToggleBtnCommand))
            {
                _highlightsOn = !_highlightsOn;

                if(!_highlightsOn)
                {
                    _host.MainWindow.RefreshEntriesList();
                }

                _host.MainWindow.UpdateUI(false, null, false, null, true, null, false);
            }
        }

        private void MainWindow_UIStateUpdated(object sender, EventArgs e)
        {
            if (_highlightsOn)
            {
                //This method iterates through all the entries and either highlights
                //them if highlighting is on, or unhighlights if it is off.
                ListView lv = (_host.MainWindow.Controls.Find(
                    "m_lvEntries", true)[0] as ListView);
                if (lv == null) { Debug.Assert(false); return; }

                lv.BeginUpdate();

                foreach (ListViewItem lvi in lv.Items)
                {
                    PwListItem li = (lvi.Tag as PwListItem);
                    if (li == null) { Debug.Assert(false); continue; }

                    PwEntry pe = li.Entry;
                    if (pe == null) { Debug.Assert(false); continue; }

                    ProtectedString pStr = pe.Strings.Get(PwDefs.PasswordField);
                    if (pStr == null) { Debug.Assert(false); continue; }

                    string pw = pStr.ReadString();

                    if (pw.IndexOf('{') >= 0)
                    {
                        //It is a reference to another entry.
                        PwDatabase pd = null;
                        try
                        {
                            pd = _host.MainWindow.DocumentManager.SafeFindContainerOf(pe);
                        }
                        catch (Exception) { Debug.Assert(false); }

                        SprContext context = new SprContext(pe, pd, (SprCompileFlags.Deref | SprCompileFlags.TextTransforms), false, false);
                        pw = SprEngine.Compile(pw, context);
                    }

                    uint bits = QualityEstimation.EstimatePasswordBits(pw.ToCharArray());
                    foreach(KeyValuePair<uint, Color> kvp in QualityDelimiter)
                    {
                        if (bits <= kvp.Key)
                        {
                            lvi.BackColor = kvp.Value;
                            break;
                        }
                    }
                }

                lv.EndUpdate();
            }
        }
    }
}