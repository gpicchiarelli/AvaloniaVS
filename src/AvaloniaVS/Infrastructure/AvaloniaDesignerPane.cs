﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Avalonia.Designer;
using AvaloniaVS.Controls;
using AvaloniaVS.Helpers;
using AvaloniaVS.IntelliSense;
using AvaloniaVS.Internals;
using AvaloniaVS.ViewModels;
using AvaloniaVS.Views;
using Debugger = System.Diagnostics.Debugger;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AvaloniaVS.Infrastructure
{
    [ComVisible(true), Guid("75b0ba12-1e01-4f80-a035-32239896bcab")]
    public partial class AvaloniaDesignerPane : WindowPane
    {
        private const int WM_KEYFIRST = 0x0100;
        private const int WM_KEYLAST = 0x0109;

        private AvaloniaDesignerHostView _designerHostView;
        private AvaloniaDesignerHostViewModel _designerHost;
        private readonly string _fileName;
        private readonly IAvaloniaDesignerSettings _designerSettings;
        private AvaloniaDesigner _designer;
        private string _targetExe;
        private long _lastRestartToken;
        private IVsCodeWindow _vsCodeWindow;
        private readonly ITextBuffer _textBuffer;

        public AvaloniaDesignerPane(IVsCodeWindow vsCodeWindow, IVsTextLines textBuffer, string fileName, IAvaloniaDesignerSettings designerSettings)
        {
            _vsCodeWindow = vsCodeWindow;
            _textBuffer = textBuffer.GetTextBuffer();
            _fileName = fileName;
            _designerSettings = designerSettings;
        }

        protected override void Initialize()
        {
            base.Initialize();
            InitializePane();
            RegisterMenuCommands();
        }

        private void InitializePane()
        {
            // initialize the designer host view.
            _designerHost = new AvaloniaDesignerHostViewModel(_fileName)
            {
                EditView = ((WindowPane) _vsCodeWindow).Content,
                Orientation = _designerSettings.SplitOrientation == SplitOrientation.Default ||
                              _designerSettings.SplitOrientation == SplitOrientation.Horizontal
                    ? Orientation.Horizontal
                    : Orientation.Vertical,
                IsReversed = _designerSettings.IsReversed
            };
            _designerHostView = new AvaloniaDesignerHostView {DataContext = _designerHost};
            _designerHostView.Init(_designerSettings);

            InitializeDesigner();
            _designerHost.TargetExeChanged += UpdateTargetExe;
        }

        void UpdateTargetExe(string exe)
        {
            _targetExe = exe;
            _designer.TargetExe = exe;
            _designerHost.DesignView = _targetExe == null
                ? (object) new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Text = $"{Path.GetFileName(_fileName)} cannot be edited in the Design view. Make sure that it's referenced from at least one desktop exe project"
                }
                : _designer;
        }

        private void InitializeDesigner()
        {
            _designer = new AvaloniaDesigner();
            UpdateTargetExe(_designerHost.TargetExe);
            _designer.Xaml = _textBuffer.CurrentSnapshot.GetText();
            AvaloniaBuildEvents.Instance.BuildEnd += Restart;
            AvaloniaBuildEvents.Instance.ModeChanged += OnModeChanged;
            _textBuffer.PostChanged += OnTextBufferPostChanged;
            ReloadMetadata();
        }

        private void OnTextBufferPostChanged(object sender, EventArgs e)
        {
            var buffer = (ITextBuffer)sender;
            _designer.Xaml = buffer.CurrentSnapshot.GetText();
        }

        protected override void OnClose()
        {
            _vsCodeWindow.Close();
            base.OnClose();
        }

        void ReloadMetadata()
        {
            if (!File.Exists(_targetExe))
            {
                return;
            }

            _textBuffer.Properties[typeof (Metadata)] = MetadataLoader.LoadMetadata(_targetExe);
        }

        public override object Content => _designerHostView;

        private void EventsOnBuildBegin()
        {
            _designer?.KillProcess();
        }

        private void OnModeChanged()
        {
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            if (dte.Mode == vsIDEMode.vsIDEModeDesign)
                Restart();
        }

        private async void Restart()
        {
            long token = ++_lastRestartToken;
            Console.WriteLine("Designer restart requested, waiting");
            await System.Threading.Tasks.Task.Delay(1000);
            if (token != _lastRestartToken)
                return;
            var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
            if (dte.Mode != vsIDEMode.vsIDEModeDesign)
                return;
            try
            {
                Console.WriteLine("Restarting designer");
                _designer?.RestartProcess();
            }
            catch
            {
                //TODO: Log
            }
            try
            {
                ReloadMetadata();
            }
            catch
            {
                //TODO: Log
            }
        }

        protected override void Dispose(bool disposing)
        {
            PaneDispose(disposing);
            base.Dispose(disposing);
        }

        private void PaneDispose(bool disposing)
        {
            if (disposing)
            {
                AvaloniaBuildEvents.Instance.BuildEnd -= Restart;
                AvaloniaBuildEvents.Instance.ModeChanged -= OnModeChanged;
                _designer?.KillProcess();
                _textBuffer.PostChanged -= OnTextBufferPostChanged;
            }
        }
    }
}