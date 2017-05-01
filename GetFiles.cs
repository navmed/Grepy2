﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Runtime.InteropServices;
using System.Threading;
using System.IO;

namespace Grepy2
{
	class GetFiles
	{
		[return: MarshalAs(UnmanagedType.Bool)]
		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		static extern bool PostMessage(HandleRef hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("Everything32.dll")]
		public static extern void Everything_Reset();
        [DllImport("Everything32.dll", CharSet = CharSet.Unicode)]
		public static extern int Everything_SetSearchW(string lpSearchString);
		[DllImport("Everything32.dll")]
		public static extern void Everything_SortResultsByPath();
		[DllImport("Everything32.dll")]
		public static extern bool Everything_QueryW(bool bWait);
		[DllImport("Everything32.dll")]
		public static extern int Everything_GetNumFileResults();
        [DllImport("Everything32.dll", CharSet = CharSet.Unicode)]
        public static extern void Everything_GetResultFullPathNameW(int nIndex, StringBuilder lpString, int nMaxCount);

		private static HandleRef FormHandle;

		List<string> filenames;
		List<string> folders;

		public GetFiles(IntPtr InHandle)
		{
			FormHandle = new HandleRef(this, InHandle);

			Globals.GetFiles.WaitHandle = new AutoResetEvent(false);  // create a WaitHandle for syncronization

			Globals.GetFiles.bIsWorking = false;
			Globals.GetFiles.bShouldStopCurrentJob = false;
			Globals.GetFiles.bShouldExit = false;
		}

		public void Run()
		{
			while( !Globals.GetFiles.bShouldExit )
			{
				Globals.GetFiles.WaitHandle.WaitOne();  // wait for something to do

				Globals.GetFiles.bIsWorking = true;

				Globals.SearchDirectoryCount = 0;

				filenames = new List<string>();
				folders = new List<string>();

				bool bUseInternalGetFiles = true;  // assume we want to use the GetFiles method if Everything fails

				if( Globals.bIsEverythingRunning )  // if using the Everything search engine...
				{
					Everything_Reset();

					// trim off any trailing backslashes on the search directory
					string search_directory = Globals.SearchDirectory.TrimEnd('\\');

					string everything_search;
					if( Globals.bRecursive )
					{
						everything_search = string.Format("\"{0}\\*\" file:wholefilename:nocase:noregex:<", search_directory);
					}
					else
					{
						string[] paths = search_directory.Split('\\');
						everything_search = string.Format("\"{0}*\" parents:{1} file:wholefilename:nocase:noregex:<", search_directory, paths.Length);
					}

					for( int i = 0; i < Globals.FileSpecs.Count; i++ )
					{
						if( i > 0 )
						{
							everything_search += " | ";
						}
						everything_search += Globals.FileSpecs[i];
					}

					everything_search = everything_search + ">";

					Everything_SetSearchW(everything_search);  // set the search parameter (quoted folder name followed by '<' 

					if( Everything_QueryW(true) )  // wait for the results
					{
						bUseInternalGetFiles = false;  // don't use the GetFiles thread

						Everything_SortResultsByPath();  // sort the results by path

						int SearchFilesCount = Everything_GetNumFileResults();

						if( SearchFilesCount > 0 )
						{
							const int bufsize = 260; 
							StringBuilder buf = new StringBuilder(bufsize);

							for( int i = 0; i < SearchFilesCount; i++ )
							{
								Everything_GetResultFullPathNameW(i, buf, bufsize);

								filenames.Add(buf.ToString());
							}
						}
					}
				}

				if( bUseInternalGetFiles )
				{
					InternalGetFiles(Globals.SearchDirectory);
				}

				if( !Globals.GetFiles.bShouldExit )
				{
					Globals.SearchFiles = new Globals.SearchFile[filenames.Count];

					for( int i = 0; (i < filenames.Count) && !Globals.GetFiles.bShouldStopCurrentJob; i++ )
					{
						Globals.SearchFile search_file = new Globals.SearchFile();

						search_file.Status = Globals.SearchFileStatus.NotProcessed;
						search_file.Filename = filenames[i];
						search_file.BaseFilename = Path.GetFileName(filenames[i]);
						search_file.FileTypeName = "";
						search_file.FolderName = Path.GetDirectoryName(filenames[i]);
						search_file.SearchMatchCount = 0;
						search_file.FileLength = 0;
						search_file.FileLastWriteTime = DateTime.MinValue;
						search_file.Lines = null;
						search_file.Matches = 0;

						Globals.SearchFiles[i] = search_file;

						if( !folders.Contains(search_file.FolderName) )  // keep track of the number of folders that will need to be searched
						{
							folders.Add(search_file.FolderName);
						}
					}

					Globals.SearchDirectoryCount = folders.Count;

					Globals.GetFiles.bIsWorking = false;

					PostMessage(FormHandle, Globals.WM_GETFILES_NOTIFY_MAIN_THREAD, IntPtr.Zero, IntPtr.Zero);  // notify main Form thread that we are done (or were cancelled)
				}
			}
		}

		private void InternalGetFiles(string InDirectory)
		{
			try
			{
				for( int index = 0; index < Globals.FileSpecs.Count; index++ )
				{
					if( Globals.GetFiles.bShouldStopCurrentJob || Globals.GetFiles.bShouldExit )
					{
						return;
					}

					string[] fileList = Directory.GetFiles(InDirectory, Globals.FileSpecs[index]);

					if( fileList != null )
					{
						for( int i=0; i < fileList.Length; i++ )
						{
							if( Globals.GetFiles.bShouldStopCurrentJob || Globals.GetFiles.bShouldExit )
							{
								return;
							}

							filenames.Add(fileList[i]);
						}
					}
				}
			}
			catch( Exception e )
			{
				Console.WriteLine("InternalGetFiles() - Directory.GetFiles Exception: {0}", e.Message);
				return;
			}

			if( Globals.bRecursive )
			{
				try
				{
					// recursively search any directories within the current folder
					string[] dirList = Directory.GetDirectories(InDirectory);

					for( int i=0; i < dirList.Length; i++ )
					{
						if( Globals.GetFiles.bShouldStopCurrentJob )
						{
							return;
						}

						string BaseFilename = Path.GetFileName( dirList[i] );
						if( (BaseFilename == "$RECYCLE.BIN") || (BaseFilename == "System Volume Information") )  // skip the recycle bin and volume information on any drive
						{
							continue;
						}

						InternalGetFiles( dirList[i] );
					}
				}
				catch (Exception e)
				{
					Console.WriteLine("InternalGetFiles() - Directory.GetDirectories Exception: {0}", e.Message);
				}
			}
		}
	}
}
