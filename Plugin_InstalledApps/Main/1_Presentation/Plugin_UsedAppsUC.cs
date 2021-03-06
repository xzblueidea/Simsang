﻿using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Collections;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Reflection;

using Simsang.Plugin;
using Plugin.Main.Applications;
using Plugin.Main.Applications.Config;
using MngApplication = Plugin.Main.Applications.ManageApplications;


namespace Plugin.Main
{
  public partial class PluginUsedAppsUC : UserControl, IPlugin, IObserver
  {

    #region MEMBERS

    private List<String> cTargetList;
    private BindingList<ApplicationRecord> cApplications;
    public List<MngApplication.ApplicationPattern> cApplicationPatterns;
    private List<String> cDataBatch;
    private String cPatternFilePath = @"plugins\UsedApps\Plugin_UsedApps_Patterns.xml";
    private TaskFacade cTask;
    private PluginParameters cPluginParams;

    #endregion


    #region PROPERTIES

    public Control PluginControl { get { return (this); } }
    public IPluginHost PluginHost { get { return cPluginParams.HostApplication; } }

    #endregion


    #region PUBLIC

    public PluginUsedAppsUC(PluginParameters pPluginParams)
    {
      InitializeComponent();


      #region DATAGRID HEADER

      DataGridViewTextBoxColumn cMACCol = new DataGridViewTextBoxColumn();
      cMACCol.DataPropertyName = "SrcMAC";
      cMACCol.Name = "SrcMAC";
      cMACCol.HeaderText = "MAC address";
      cMACCol.ReadOnly = true;
      cMACCol.Visible = true;
      cMACCol.Width = 140;
      DGV_Applications.Columns.Add(cMACCol);


      DataGridViewTextBoxColumn cSrcIPCol = new DataGridViewTextBoxColumn();
      cSrcIPCol.DataPropertyName = "SrcIP";
      cSrcIPCol.Name = "SrcIP";
      cSrcIPCol.HeaderText = "Source IP";
      //cSrcIPCol.Visible = false;
      cSrcIPCol.ReadOnly = true;
      cSrcIPCol.Width = 120;
      DGV_Applications.Columns.Add(cSrcIPCol);


      DataGridViewTextBoxColumn cAppNameCol = new DataGridViewTextBoxColumn();
      cAppNameCol.DataPropertyName = "AppName";
      cAppNameCol.Name = "AppName";
      cAppNameCol.HeaderText = "Application name";
      cAppNameCol.ReadOnly = true;
      cAppNameCol.Visible = true;
      cAppNameCol.Width = 160;
      DGV_Applications.Columns.Add(cAppNameCol);

      DataGridViewTextBoxColumn cAppURLCol = new DataGridViewTextBoxColumn();
      cAppURLCol.DataPropertyName = "AppURL";
      cAppURLCol.Name = "AppURL";
      cAppURLCol.HeaderText = "Application URL";
      cAppURLCol.ReadOnly = true;
      cAppURLCol.Visible = true;
      //            cAppURLCol.Width = 230; // 213;
      cAppURLCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
      DGV_Applications.Columns.Add(cAppURLCol);



      cApplications = new BindingList<ApplicationRecord>();
      DGV_Applications.DataSource = cApplications;

      #endregion


      /*
       * Plugin configuration
       */
      T_GUIUpdate.Interval = 1000;
      cPluginParams = pPluginParams;
      String lBaseDir = String.Format(@"{0}\", (pPluginParams != null) ? pPluginParams.PluginDirectoryFullPath : Directory.GetCurrentDirectory());
      String lSessionDir = (pPluginParams != null) ? pPluginParams.SessionDirectoryFullPath : String.Format("{0}sessions", lBaseDir);

      Config = new PluginProperties()
      {
        BaseDir = lBaseDir,
        SessionDir = lSessionDir,
        PluginName = "Used apps",
        PluginDescription = "Listing with installed applications per client system.",
        PluginVersion = "0.7",
        Ports = "TCP:80;UDP:53;",
        IsActive = true
      };

      cDataBatch = new List<String>();

      // Make it double buffered.
      typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, DGV_Applications, new object[] { true });
      T_GUIUpdate.Start();
      
      cApplicationPatterns = new List<MngApplication.ApplicationPattern>();
      cTask = TaskFacade.getInstance(this);
      DomainFacade.getInstance(this).addObserver(this);
    }

    #endregion


    #region PRIVATE


    /// <summary>
    /// 
    /// </summary>
    public void ProcessEntries()
    {
      if (cDataBatch != null && cDataBatch.Count > 0)
      {
        List<ApplicationRecord> lNewRecords = new List<ApplicationRecord>();
        List<String> lNewData;
        Match lMatchURI;
        Match lMatchHost;
        String lRemoteHost = String.Empty;
        String lReqString = String.Empty;
        String lRemotePort = "0";
        String lRemoteString = String.Empty;
        String lProto = String.Empty;
        String lSMAC = String.Empty;
        String lSIP = String.Empty;
        String lSPort = String.Empty;
        String lDIP = String.Empty;
        String lDPort = String.Empty;
        String lData = String.Empty;
        String[] lSplitter;

        lock (this)
        {
          lNewData = new List<String>(cDataBatch);
          cDataBatch.Clear();
        } // lock (this)...


        foreach (String lEntry in lNewData)
        {
          try
          {
            if (!String.IsNullOrEmpty(lEntry))
            {
              if ((lSplitter = Regex.Split(lEntry, @"\|\|")).Length == 7)
              {
                lProto = lSplitter[0];
                lSMAC = lSplitter[1];
                lSIP = lSplitter[2];
                lSPort = lSplitter[3];
                lDIP = lSplitter[4];
                lDPort = lSplitter[5];
                lData = lSplitter[6];
   
                if (lProto == "TCP" && lDPort == "80" &&
                    ((lMatchURI = Regex.Match(lData, @"(\s+|^)(GET|POST|HEAD)\s+([^\s]+)\s+HTTP\/"))).Success &&
                    ((lMatchHost = Regex.Match(lData, @"\.\.Host\s*:\s*([\w\d\.]+?)\.\.", RegexOptions.IgnoreCase))).Success)
                {
                  lRemotePort = "80";
                  lRemoteHost = lMatchHost.Groups[1].Value.ToString();
                  lReqString = lMatchURI.Groups[3].Value.ToString();

                  lRemoteString = lRemoteHost + ":" + lRemotePort + lReqString;
                }
                else if (lProto == "DNSREQ" && lDPort == "53")
                {
                  lRemoteString = lData;
                }


                /*
                 * Browse through patterns to identify the app
                 */
                if (lRemoteString.Length > 5)
                {
                  foreach (MngApplication.ApplicationPattern lPattern in cApplicationPatterns)
                  {
                    if (Regex.Match(lRemoteString, @lPattern.ApplicationPatternString).Success)
                    {
                      try
                      {
                        cTask.addRecord(new ApplicationRecord(lSMAC, lSIP, lDPort, lRemoteHost, lReqString, lPattern.ApplicationName, lPattern.CompanyURL));
                      }
                      catch (Exception lEx)
                      {
                        cPluginParams.HostApplication.LogMessage(String.Format("{0}: {1}", Config.PluginName, lEx.Message)); 
                      }
                    } // if (lSplit2.L...
                  } //foreach (st...                 
                } // if (lRemoteString...
              } // if (lSplitte...
            } // if (pData.Leng...
          }
          catch (Exception lEx)
          {
            cPluginParams.HostApplication.LogMessage(String.Format("{0}: {1}", Config.PluginName, lEx.Message));
          }
        } // foreach (...
      } // if (cDataBa...
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="pMAC"></param>
    /// <param name="pAppName"></param>
    /// <returns></returns>
    private bool ListEntryExists(String pMAC, String pAppName)
    {
      bool lRetVal = false;

      foreach (ApplicationRecord lApp in cApplications)
      {
        if (lApp.SrcMAC == pMAC && lApp.AppName == pAppName)
        {
          lRetVal = true;
          break;
        } // if (lApp.Src...
      } // foreach (Applic...

      return (lRetVal);
    }


    #endregion


    #region IPlugin Member


    /// <summary>
    /// 
    /// </summary>
    public PluginProperties Config { set; get; }


    /// <summary>
    /// 
    /// </summary>
    public delegate void onInitDelegate();
    public void onInit()
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onInitDelegate(onInit), new object[] { });
        return;
      } // if (InvokeRequired)


      cPluginParams.HostApplication.Register(this);
      cPluginParams.HostApplication.PluginSetStatus(this, "grey");

      cApplicationPatterns = cTask.readApplicationPatterns();
    }



    /// <summary>
    /// 
    /// </summary>
    public delegate void onStartAttackDelegate();
    public void onStartAttack()
    {
      if (Config.IsActive)
      {
        if (InvokeRequired)
        {
          BeginInvoke(new onStartAttackDelegate(onStartAttack), new object[] { });
          return;
        } // if (InvokeRequired)

        cPluginParams.HostApplication.PluginSetStatus(this, "green");
      } // if (cIsActiv...
    }



    /// <summary>
    /// 
    /// </summary>
    public delegate void onStopAttackDelegate();
    public void onStopAttack()
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onStopAttackDelegate(onStopAttack), new object[] { });
        return;
      } // if (InvokeRequired)

      cPluginParams.HostApplication.PluginSetStatus(this, "grey");
    }


    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public delegate String getDataDelegate();
    public String getData()
    {
      if (InvokeRequired)
      {
        BeginInvoke(new getDataDelegate(getData), new object[] { });
        return ("");
      } // if (InvokeRequired)

      return ("");
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="pSessionID"></param>
    /// <returns></returns>
    public delegate String onGetSessionDataDelegate(String pSessionID);
    public String onGetSessionData(String pSessionID)
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onGetSessionDataDelegate(onGetSessionData), new object[] { pSessionID });
        return (String.Empty);
      } // if (InvokeRequired)

      String lRetVal = String.Empty;

      lRetVal = cTask.getSessionData(pSessionID);

      return (lRetVal);
    }



    /// <summary>
    /// 
    /// </summary>
    public delegate void onResetPluginDelegate();
    public void onResetPlugin()
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onResetPluginDelegate(onResetPlugin), new object[] { });
        return;
      } // if (InvokeRequired)

      cTask.removeAllRecords();
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="pSessionName"></param>
    public delegate void onLoadSessionDataFromFileDelegate(String pSessionName);
    public void onLoadSessionDataFromFile(String pSessionName)
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onLoadSessionDataFromFileDelegate(onLoadSessionDataFromFile), new object[] { pSessionName });
        return;
      } // if (InvokeRequired)

      try
      {
        onResetPlugin();
      }
      catch (Exception lEx)
      {
        cPluginParams.HostApplication.LogMessage(String.Format("{0}: {1}", Config.PluginName, lEx.Message));
      }

      try
      {
        cTask.loadSessionData(pSessionName);
      }
      catch (Exception lEx)
      {
        cPluginParams.HostApplication.LogMessage(String.Format("{0}: {1}", Config.PluginName, lEx.Message)); 
      }
    }



    /// <summary>
    /// 
    /// </summary>
    /// <param name="pSessionData"></param>
    public delegate void onLoadSessionDataFromStringDelegate(String pSessionData);
    public void onLoadSessionDataFromString(String pSessionData)
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onLoadSessionDataFromStringDelegate(onLoadSessionDataFromString), new object[] { pSessionData });
        return;
      } // if (InvokeRequired)

      cTask.loadSessionDataFromString(pSessionData);
    }



    /// <summary>
    /// Serialize session data
    /// </summary>
    /// <param name="pSessionName"></param>
    public delegate void onSaveSessionDataDelegate(String pSessionName);
    public void onSaveSessionData(String pSessionName)
    {
      if (Config.IsActive)
      {
        if (InvokeRequired)
        {
          BeginInvoke(new onSaveSessionDataDelegate(onSaveSessionData), new object[] { pSessionName });
          return;
        } // if (InvokeRequired)

        cTask.saveSession(pSessionName);
      } // if (cIsActiv...
    }



    /// <summary>
    /// Remove session file with serialized data. 
    /// </summary>
    /// <param name="pSessionName"></param>
    public delegate void onDeleteSessionDataDelegate(String pSessionFileName);
    public void onDeleteSessionData(String pSessionName)
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onDeleteSessionDataDelegate(onDeleteSessionData), new object[] { pSessionName });
        return;
      } // if (InvokeRequired)

      cTask.deleteSession(pSessionName);
    }



    /// <summary>
    /// 
    /// </summary>
    public delegate void onShutDownDelegate();
    public void onShutDown()
    {
      if (InvokeRequired)
      {
        BeginInvoke(new onShutDownDelegate(onShutDown), new object[] { });
        return;
      } // if (Invoke
    }


    /// <summary>
    /// New input data arrived
    /// TCP||00:11:22:33:44:55||192.168.0.123||51984||74.125.79.136||80||GET...
    /// </summary>
    /// <param name="pData"></param>
    public delegate void onNewDataDelegate(String pData);
    public void onNewData(String pData)
    {
      if (Config.IsActive)
      {
        if (InvokeRequired)
        {
          BeginInvoke(new onNewDataDelegate(onNewData), new object[] { pData });
          return;
        } // if (InvokeRequired)

        lock (this)
        {
          if (cDataBatch != null && pData != null && pData.Length > 0)
            cDataBatch.Add(pData);
        } // lock (this)
      } // if (cIsActi...
    }



    /// <summary>
    /// 
    /// </summary>
    /// <param name="pTargetList"></param>
    public void SetTargets(List<String> pTargetList)
    {
      cTargetList = pTargetList;
    }


    #endregion


    #region EVENTS

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void T_GUIUpdate_Tick(object sender, EventArgs e)
    {
      ProcessEntries();
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TSMI_Clear_Click(object sender, EventArgs e)
    {
      cTask.removeAllRecords();
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DGV_Applications_MouseUp(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right)
      {
        try
        {
          DataGridView.HitTestInfo hti = DGV_Applications.HitTest(e.X, e.Y);
          if (hti.RowIndex >= 0)
            CMS_Applications.Show(DGV_Applications, e.Location);
        }
        catch (Exception lEx) { }
      }
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void deleteEntryToolStripMenuItem_Click(object sender, EventArgs e)
    {
      try
      {
        int lCurIndex = DGV_Applications.CurrentCell.RowIndex;
        cTask.removeRecordAt(lCurIndex);
      }
      catch (Exception lEx)
      {
        cPluginParams.HostApplication.LogMessage(String.Format("{0}: {1}", Config.PluginName, lEx.Message));
      }
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DGV_Applications_DoubleClick(object sender, EventArgs e)
    {
      MngApplication.Form_ManageApps lManageApps = new MngApplication.Form_ManageApps(this);
      lManageApps.ShowDialog();
      cApplicationPatterns = cTask.readApplicationPatterns();
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DGV_Applications_MouseDown(object sender, MouseEventArgs e)
    {
      try
      {
        DataGridView.HitTestInfo hti = DGV_Applications.HitTest(e.X, e.Y);

        if (hti.RowIndex >= 0)
        {
          DGV_Applications.ClearSelection();
          DGV_Applications.Rows[hti.RowIndex].Selected = true;
          DGV_Applications.CurrentCell = DGV_Applications.Rows[hti.RowIndex].Cells[0];
        }
      }
      catch (Exception)
      {
        DGV_Applications.ClearSelection();
      }
    }

    #endregion


    #region OBSERVER INTERFACE METHODS

    public void update(List<ApplicationRecord> pRecordList)
    {
      bool lIsLastLine = false;
      int lLastPosition = -1;
      int lLastRowIndex = -1;
      int lSelectedIndex = -1;


      lock (this)
      {
        /*
         * Remember DGV positions
         */
        if (DGV_Applications.CurrentRow != null && DGV_Applications.CurrentRow == DGV_Applications.Rows[DGV_Applications.Rows.Count - 1])
          lIsLastLine = true;

        lLastPosition = DGV_Applications.FirstDisplayedScrollingRowIndex;
        lLastRowIndex = DGV_Applications.Rows.Count - 1;

        if (DGV_Applications.CurrentCell != null)
          lSelectedIndex = DGV_Applications.CurrentCell.RowIndex;

        cApplications.Clear();
        foreach (ApplicationRecord lTmp in pRecordList)
          cApplications.Add(lTmp);

        // Selected cell/row
        try
        {
          if (lSelectedIndex >= 0)
            DGV_Applications.CurrentCell = DGV_Applications.Rows[lSelectedIndex].Cells[0];
        }
        catch (Exception) { }


        // Reset position
        try
        {
          if (lLastPosition >= 0)
            DGV_Applications.FirstDisplayedScrollingRowIndex = lLastPosition;
        }
        catch (Exception) { }

        DGV_Applications.Refresh();
      } // lock (thi...
    }

    #endregion

  }
}
