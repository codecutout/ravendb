﻿// -----------------------------------------------------------------------
//  <copyright file="IncrementalBackup.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Voron.Impl.Backup;
using Voron.Impl.Paging;
using Xunit;

namespace Voron.Tests.Backups
{
	public class Incremental : StorageTest
	{
		private Func<int, string> _incrementalBackupFile = n => string.Format("voron-test.{0}-incremental-backup.zip", n);
		private const string _restoredStoragePath = "incremental-backup-test.data";

		protected override void Configure(StorageEnvironmentOptions options)
		{
			options.MaxLogFileSize = 1000 * AbstractPager.PageSize;
			options.IncrementalBackupEnabled = true;
			options.ManualFlushing = true;
		}

		public Incremental()
		{
			Clean();
		}

		[Fact]
		public void CanBackupAndRestoreOnEmptyStorage()
		{
            RequireFileBasedPager();

			var random = new Random();
			var buffer = new byte[8192];
			random.NextBytes(buffer);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < 500; i++)
				{
					tx.State.Root.Add("items/" + i, new MemoryStream(buffer));
				}

				tx.Commit();
			}

			BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(0));

			var options = StorageEnvironmentOptions.ForPath(_restoredStoragePath);
			options.MaxLogFileSize = Env.Options.MaxLogFileSize;

            BackupMethods.Incremental.Restore(options, new[] { _incrementalBackupFile(0) });

			using (var env = new StorageEnvironment(options))
			{

				using (var tx = env.NewTransaction(TransactionFlags.Read))
				{
					for (int i = 0; i < 500; i++)
					{
						var readResult = tx.State.Root.Read("items/" + i);
						Assert.NotNull(readResult);
						var memoryStream = new MemoryStream();
						readResult.Reader.CopyTo(memoryStream);
						Assert.Equal(memoryStream.ToArray(), buffer);
					}
				}
			}
		}

		[Fact]
		public void CanDoMultipleIncrementalBackupsAndRestoreOneByOne()
		{
            RequireFileBasedPager();

			var random = new Random();
			var buffer = new byte[1024];
			random.NextBytes(buffer);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < 300; i++)
				{
					tx.State.Root.Add("items/" + i, new MemoryStream(buffer));
				}

				tx.Commit();
			}

			BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(0));

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 300; i < 600; i++)
				{
					tx.State.Root.Add("items/" + i, new MemoryStream(buffer));
				}

				tx.Commit();
			}

			BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(1));

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 600; i < 1000; i++)
				{
					tx.State.Root.Add("items/" + i, new MemoryStream(buffer));
				}

				tx.Commit();
			}

			Env.FlushLogToDataFile(); // make sure that incremental backup will work even if we flushed journals to the data file

			BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(2));

			var options = StorageEnvironmentOptions.ForPath(_restoredStoragePath);
			options.MaxLogFileSize = Env.Options.MaxLogFileSize;

            BackupMethods.Incremental.Restore(options, new[]
            {
                _incrementalBackupFile(0),
                _incrementalBackupFile(1),
                _incrementalBackupFile(2)
            });

			using (var env = new StorageEnvironment(options))
			{
				using (var tx = env.NewTransaction(TransactionFlags.Read))
				{
					for (int i = 0; i < 1000; i++)
					{
						var readResult = tx.State.Root.Read("items/" + i);
						Assert.NotNull(readResult);
						var memoryStream = new MemoryStream();
						readResult.Reader.CopyTo(memoryStream);
						Assert.Equal(memoryStream.ToArray(), buffer);
					}
				}
			}
		}

		[Fact]
		public void IncrementalBackupShouldCopyJustNewPagesSinceLastBackup()
        {
            RequireFileBasedPager();
			var random = new Random();
			var buffer = new byte[100];
			random.NextBytes(buffer);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < 5; i++)
				{
					tx.State.Root.Add("items/" + i, new MemoryStream(buffer));
				}

				tx.Commit();
			}

		    var usedPagesInJournal = Env.Journal.CurrentFile.WritePagePosition;

			var backedUpPages = BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(0));

			Assert.Equal(usedPagesInJournal, backedUpPages);

			var writePos = Env.Journal.CurrentFile.WritePagePosition;
		
			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 5; i < 10; i++)
				{
					tx.State.Root.Add("items/" + i, new MemoryStream(buffer));
				}

				tx.Commit();
			}

			var usedByLastTransaction = Env.Journal.CurrentFile.WritePagePosition - writePos;

			backedUpPages = BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(1));

			Assert.Equal(usedByLastTransaction, backedUpPages);

			var options = StorageEnvironmentOptions.ForPath(_restoredStoragePath);
			options.MaxLogFileSize = Env.Options.MaxLogFileSize;

            BackupMethods.Incremental.Restore(options, new[]
            {
                _incrementalBackupFile(0),
                _incrementalBackupFile(1)
            });

			using (var env = new StorageEnvironment(options))
			{
				using (var tx = env.NewTransaction(TransactionFlags.Read))
				{
					for (int i = 0; i < 10; i++)
					{
						var readResult = tx.State.Root.Read("items/" + i);
						Assert.NotNull(readResult);
						var memoryStream = new MemoryStream();
						readResult.Reader.CopyTo(memoryStream);
						Assert.Equal(memoryStream.ToArray(), buffer);
					}
				}
			}
		}

        [Fact]
        public void IncrementalBackupShouldAcceptEmptyIncrementalBackups()
        {
            RequireFileBasedPager();
            var random = new Random();
            var buffer = new byte[100];
            random.NextBytes(buffer);

            using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
            {
                for (int i = 0; i < 5; i++)
                {
                    tx.State.Root.Add("items/" + i, new MemoryStream(buffer));
                }

                tx.Commit();
            }

            var usedPagesInJournal = Env.Journal.CurrentFile.WritePagePosition;

            var backedUpPages = BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(0));

            Assert.Equal(usedPagesInJournal, backedUpPages);

            // We don't modify anything between backups - to create empty incremental backup

            var writePos = Env.Journal.CurrentFile.WritePagePosition;

            var usedByLastTransaction = Env.Journal.CurrentFile.WritePagePosition - writePos;
            Assert.Equal(0, usedByLastTransaction);

            backedUpPages = BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(1));

            Assert.Equal(usedByLastTransaction, backedUpPages);

            var options = StorageEnvironmentOptions.ForPath(_restoredStoragePath);
            options.MaxLogFileSize = Env.Options.MaxLogFileSize;

            BackupMethods.Incremental.Restore(options, new[]
            {
                _incrementalBackupFile(0),
                _incrementalBackupFile(1)
            });

            using (var env = new StorageEnvironment(options))
            {
                using (var tx = env.NewTransaction(TransactionFlags.Read))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var readResult = tx.State.Root.Read("items/" + i);
                        Assert.NotNull(readResult);
                        var memoryStream = new MemoryStream();
                        readResult.Reader.CopyTo(memoryStream);
                        Assert.Equal(memoryStream.ToArray(), buffer);
                    }
                }
            }
        }

		[Fact]
		public void IncorrectWriteOfOverflowPagesFromJournalsToDataFile_RavenDB_2806()
		{
			RequireFileBasedPager();

			const int testedOverflowSize = 20000;

			var overflowValue = new byte[testedOverflowSize];
			new Random(1).NextBytes(overflowValue);


			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				var tree = Env.CreateTree(tx, "test");

				var itemBytes = new byte[16000];

				new Random(2).NextBytes(itemBytes);
				tree.Add("items/1", itemBytes);

				new Random(3).NextBytes(itemBytes);
				tree.Add("items/2", itemBytes);

				tree.Delete("items/1");
				tree.Delete("items/2");

				tree.Add("items/3", overflowValue);

				tx.Commit();
			}

			BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(0));

			var options = StorageEnvironmentOptions.ForPath(_restoredStoragePath);
			options.MaxLogFileSize = Env.Options.MaxLogFileSize;

			BackupMethods.Incremental.Restore(options, new[]
            {
                _incrementalBackupFile(0)
            });

			using (var env = new StorageEnvironment(options))
			{
				using (var tx = env.NewTransaction(TransactionFlags.Read))
				{
					var tree = tx.ReadTree("test");

					var readResult = tree.Read("items/3");

					var readBytes = new byte[testedOverflowSize];

					readResult.Reader.Read(readBytes, 0, testedOverflowSize);

					Assert.Equal(overflowValue, readBytes);
				}
			}
		}

		[Fact]
		public void IncorrectWriteOfOverflowPagesFromJournalsToDataFile_2_RavenDB_2806()
		{
			RequireFileBasedPager();

			const int testedOverflowSize = 16000;

			var overflowValue = new byte[testedOverflowSize];
			new Random(1).NextBytes(overflowValue);


			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				var tree = Env.CreateTree(tx, "test");

				var itemBytes = new byte[2000];

				new Random(2).NextBytes(itemBytes);
				tree.Add("items/1", itemBytes);


				itemBytes = new byte[30000];
				new Random(3).NextBytes(itemBytes);
				tree.Add("items/2", itemBytes);

				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				var tree = Env.CreateTree(tx, "test");
				tree.Delete("items/1");
				tree.Delete("items/2");

				tree.Add("items/3", overflowValue);

				tx.Commit();
			}

			BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(0));

			var options = StorageEnvironmentOptions.ForPath(_restoredStoragePath);
			options.MaxLogFileSize = Env.Options.MaxLogFileSize;

			BackupMethods.Incremental.Restore(options, new[]
            {
                _incrementalBackupFile(0)
            });

			using (var env = new StorageEnvironment(options))
			{
				using (var tx = env.NewTransaction(TransactionFlags.Read))
				{
					var tree = tx.ReadTree("test");

					var readResult = tree.Read("items/3");

					var readBytes = new byte[testedOverflowSize];

					readResult.Reader.Read(readBytes, 0, testedOverflowSize);

					Assert.Equal(overflowValue, readBytes);
				}
			}
		}

		[Fact]
		public void IncorrectWriteOfOverflowPagesFromJournalsInBackupToDataFile_RavenDB_2891()
		{
			RequireFileBasedPager();

			const int testedOverflowSize = 50000;

			var overflowValue = new byte[testedOverflowSize];
			new Random(1).NextBytes(overflowValue);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				var tree = Env.CreateTree(tx, "test");

				var itemBytes = new byte[30000];

				new Random(2).NextBytes(itemBytes);
				tree.Add("items/1", itemBytes);

				new Random(3).NextBytes(itemBytes);
				tree.Add("items/2", itemBytes);

				tree.Delete("items/1");

				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				var tree = tx.ReadTree("test");

				tree.Delete("items/2");

				tree.Add("items/3", overflowValue);

				tx.Commit();
			}

			BackupMethods.Incremental.ToFile(Env, _incrementalBackupFile(0));

			var options = StorageEnvironmentOptions.ForPath(_restoredStoragePath);
			options.MaxLogFileSize = Env.Options.MaxLogFileSize;

			BackupMethods.Incremental.Restore(options, new[]
            {
                _incrementalBackupFile(0)
            });

			using (var env = new StorageEnvironment(options))
			{
				using (var tx = env.NewTransaction(TransactionFlags.Read))
				{
					var tree = tx.ReadTree("test");

					var readResult = tree.Read("items/3");

					var readBytes = new byte[testedOverflowSize];

					readResult.Reader.Read(readBytes, 0, testedOverflowSize);

					Assert.Equal(overflowValue, readBytes);
				}
			}
		}

		private void Clean()
		{
			foreach (var incBackupFile in Directory.EnumerateFiles(".", "*incremental-backup"))
			{
				File.Delete(incBackupFile);
			}

			if (Directory.Exists(_restoredStoragePath))
				Directory.Delete(_restoredStoragePath, true);
		}

		public override void Dispose()
		{
			base.Dispose();
			Clean();
		}	
	}
}