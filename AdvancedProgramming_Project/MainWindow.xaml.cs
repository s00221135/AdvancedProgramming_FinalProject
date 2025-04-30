using System;
using System.Collections.Generic;
using System.ComponentModel;            //BackgroundWorker
using System.IO;
using System.IO.IsolatedStorage;        //isolated storage
using System.Linq;
using System.Threading;
using System.Windows;

namespace AdvancedProgramming_Project;

/// <summary>
/// Fergal Feeney S00221135
/// Advanced Programming
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<MusicRecord> _library = new();
    private readonly object _libLock = new();          //monitor lock
    private readonly Queue<MusicRecord> _queue = new();
    private readonly object _queueLock = new();         //monitor lock

    [ThreadStatic]                       //per-thread static field
    private static int _threadCounter;

    //Threads
    private readonly BackgroundWorker bwSearch = new(); //Thread 2
    private Thread? tProducer;   //Thread 1 adds records
    private Thread? tConsumer;   //Thread 3 logs new records
    private Thread? tSaver;      //Thread 4 autosave

    public MainWindow()
    {
        InitializeComponent();      //UI = main thread
        InitBackgroundWorker();     //BackgroundWorker config
        StartProducer();            //Thread 1
        StartConsumer();            //Thread 3
        StartSaver();               //Thread 4
    }

    private void SetStatus(string msg) =>
        Dispatcher.Invoke(() => txtStatus.Text = msg);

    private void InitBackgroundWorker()
    {
        bwSearch.WorkerReportsProgress = true;      //progress bar
        bwSearch.WorkerSupportsCancellation = true; //cancel
        bwSearch.DoWork += Bw_DoWork;
        bwSearch.ProgressChanged += Bw_Progress;
        bwSearch.RunWorkerCompleted += Bw_Completed;
    }

    private void btnStart_Click(object s, RoutedEventArgs e)
    {
        var term = txtSearch.Text.Trim();
        if (term.Length == 0) { SetStatus("Enter a search term."); return; }

        if (!bwSearch.IsBusy)
        {
            progress.Value = 0;
            lstResults.Items.Clear();
            bwSearch.RunWorkerAsync(term);          //passes parameter
            SetStatus("Search started…");
        }
    }

    private void btnCancel_Click(object s, RoutedEventArgs e)
    {
        if (bwSearch.IsBusy) bwSearch.CancelAsync(); //cancellation
    }

    private void Bw_DoWork(object? sender, DoWorkEventArgs e)
    {
        var worker = (BackgroundWorker)sender!;
        string term = (string)e.Argument!;

        List<MusicRecord> snapshot;
        Monitor.Enter(_libLock);               //Monitor.Enter / Exit
        try { snapshot = _library.ToList(); }
        finally { Monitor.Exit(_libLock); }

        var matches = snapshot.Where(r =>
                        int.TryParse(term, out int y) ? r.Year == y
                        : r.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          r.Artist.Contains(term, StringComparison.OrdinalIgnoreCase))
                     .ToList();

        for (int i = 0; i < matches.Count; i++)
        {
            if (worker.CancellationPending) { e.Cancel = true; return; }

            worker.ReportProgress((i + 1) * 100 / matches.Count, matches[i]);

            Thread.Sleep(1000);
        }

        worker.ReportProgress(0); //reset bar
    }

    private void Bw_Progress(object? s, ProgressChangedEventArgs e)
    {
        progress.Value = e.ProgressPercentage;
        if (e.UserState is MusicRecord r)
            lstResults.Items.Add($"{r.Title} – {r.Artist} ({r.Year})");
    }

    private void Bw_Completed(object? s, RunWorkerCompletedEventArgs e)
    {
        progress.Value = e.Cancelled ? 0 : 100;
        SetStatus(e.Cancelled ? "Search cancelled." :
                  e.Error != null ? $"Error: {e.Error.Message}" : "Search finished.");
    }

    //Thread 1  (Producer)
    private void StartProducer()
    {
        tProducer = new Thread(ProducerLoop)
        { Name = "Producer", Priority = ThreadPriority.BelowNormal, IsBackground = true };
        tProducer.Start();
    }

    //Thread 1 poll UI every 5 secs
    private void ProducerLoop()
    {
        while (true)
        {
            try { Thread.Sleep(5000); }          
            catch (ThreadInterruptedException) { return; }   //exits quietly

            string title = "", artist = ""; int year = 0;
            Dispatcher.Invoke(() =>
            {
                title = txtTitle.Text.Trim();
                artist = txtArtist.Text.Trim();
                int.TryParse(txtYear.Text.Trim(), out year);
            });

            if (title == "" || artist == "" || year == 0) continue;

            var rec = new MusicRecord { Title = title, Artist = artist, Year = year };

            Monitor.Enter(_libLock);
            try { _library.Add(rec); }
            finally { Monitor.Exit(_libLock); }

            Monitor.Enter(_queueLock);
            try { _queue.Enqueue(rec); Monitor.Pulse(_queueLock); }
            finally { Monitor.Exit(_queueLock); }

            Dispatcher.Invoke(() => { txtTitle.Clear(); txtArtist.Clear(); txtYear.Clear(); });

            _threadCounter++;                        //per-thread static increment
        }
    }


    //Thread 3  (Consumer)
    private void StartConsumer()
    {
        tConsumer = new Thread(ConsumerLoop)
        { Name = "Logger", IsBackground = true };
        tConsumer.Start();
    }

    private void ConsumerLoop()
    {
        while (true)
        {
            MusicRecord? rec = null;
            Monitor.Enter(_queueLock);
            try
            {
                try { while (_queue.Count == 0) Monitor.Wait(_queueLock); }   //wait
                catch (ThreadInterruptedException) { return; }               

                rec = _queue.Dequeue();
            }
            finally { Monitor.Exit(_queueLock); }

            if (rec != null)
                Dispatcher.Invoke(() => txtStatus.Text = $"New: {rec.Title} ({rec.Year})");

            try { Thread.Sleep(200); }
            catch (ThreadInterruptedException) { return; }
        }
    }


    //Thread 4 (Autosaver)
    private void StartSaver()
    {
        tSaver = new Thread(SaverLoop)
        { Name = "AutoSaver", IsBackground = true };
        tSaver.Start();
    }

    private void SaverLoop()
    {
        while (true)
        {
            try { Thread.Sleep(30000); }         
            catch (ThreadInterruptedException) { return; } 

            SaveLibrary();

            if (Monitor.TryEnter(_libLock, 50))  
            {
                try { _ = _library.Count; }
                finally { Monitor.Exit(_libLock); }
            }
        }
    }

    //Isolated-storage save 
    private void btnSaveIso_Click(object s, RoutedEventArgs e) => SaveLibrary();
    private void btnLoadIso_Click(object s, RoutedEventArgs e) => LoadLibrary();

    private void SaveLibrary()
    {
        List<MusicRecord> snap;
        Monitor.Enter(_libLock);
        try { snap = _library.ToList(); }
        finally { Monitor.Exit(_libLock); }

        using var iso = IsolatedStorageFile.GetUserStoreForAssembly();
        using var fs = new IsolatedStorageFileStream("library.bin", FileMode.Create, iso);
        using var bw = new BinaryWriter(fs);
        bw.Write(snap.Count);
        foreach (var r in snap) { bw.Write(r.Title); bw.Write(r.Artist); bw.Write(r.Year); }
        SetStatus("Saved.");
    }

    private void LoadLibrary()
    {
        using var iso = IsolatedStorageFile.GetUserStoreForAssembly();
        if (!iso.FileExists("library.bin")) { SetStatus("No save found."); return; }

        using var fs = new IsolatedStorageFileStream("library.bin", FileMode.Open, iso);
        using var br = new BinaryReader(fs);
        int n = br.ReadInt32();
        var list = new List<MusicRecord>();
        for (int i = 0; i < n; i++)
            list.Add(new MusicRecord { Title = br.ReadString(), Artist = br.ReadString(), Year = br.ReadInt32() });

        Monitor.Enter(_libLock);
        try { _library.Clear(); _library.AddRange(list); }
        finally { Monitor.Exit(_libLock); }

        lstResults.Items.Clear();
        foreach (var r in _library)
            lstResults.Items.Add($"{r.Title} – {r.Artist} ({r.Year})");

        SetStatus($"Loaded {n} record(s).");
    }

    //shutdown
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        tProducer?.Interrupt();
        tConsumer?.Interrupt();
        tSaver?.Interrupt();
        base.OnClosing(e);
    }
}
