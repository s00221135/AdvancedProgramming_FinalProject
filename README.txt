Student: Fergal Feeney  (S00221135)
Module: Advanced Programming

What the application does

- Lets the user enter Song Title, Artist and Release Year.  
- A background “Producer” thread polls the text-boxes every 5 secs and
  stores valid songs in a shared library.  
- A “Consumer” thread logs new songs in the status bar.  
- A BackgroundWorker performs progressive searches (year or keyword),
  shows a progress-bar, supports cancellation, and outputs one match
  per second in the list-box.  
- A “Saver” thread auto-saves the library to isolated storage every
  30 secs.  The user can also Save / Load manually.  
- UI is WPF.

