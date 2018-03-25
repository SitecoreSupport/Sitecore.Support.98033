namespace Sitecore.Support.Buckets.Pipelines.UI
{
	using Sitecore;
	using Sitecore.Buckets.Managers;
	using Sitecore.Buckets.Util;
	using Sitecore.Configuration;
	using Sitecore.Data;
	using Sitecore.Data.Events;
	using Sitecore.Data.Items;
	using Sitecore.Diagnostics;
	using Sitecore.Events;
	using Sitecore.Jobs;
	using Sitecore.Links;
	using Sitecore.SecurityModel;
	using Sitecore.Shell.Applications.Dialogs.ProgressBoxes;
	using Sitecore.Shell.Framework;
	using Sitecore.Shell.Framework.Pipelines;
	using Sitecore.Web.UI.Sheer;
	using System;

	public class ItemDrag : ItemOperation
	{
		public void Execute(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			Database database = this.GetDatabase(args);
			Item source = this.GetSource(args, database);
			Item target = this.GetTarget(args);
			if (!this.IsBucket(target) && !this.IsBucket(source) && !this.IsItemContainedWithinBucket(source))
			{
				return;
			}
			if (args.Parameters["copy"] == "1")
			{
				this.CopyItems();
			}
			else
			{
				this.MoveItems();
			}
		}

		protected virtual void MoveItems()
		{
			ProgressBox.ExecuteSync("Moving Items", "~/icon/Core3/32x32/move_to_folder.png", this.StartMoveProcess, this.EndMoveProcess);
		}

		protected virtual void CopyItems()
		{
			ProgressBox.ExecuteSync("Copying Items", "~/icon/Core3/32x32/copy_to_folder.png", this.StartCopyProcess, this.EndCopyProcess);
		}

		protected virtual bool IsItemContainedWithinBucket(Item item)
		{
			Assert.ArgumentNotNull(item, "item");
			return BucketManager.IsItemContainedWithinBucket(item);
		}

		protected virtual bool IsBucket(Item item)
		{
			Assert.ArgumentNotNull(item, "item");
			return BucketManager.IsBucket(item);
		}

		protected virtual int Resort(Item target, DragAction dragAction)
		{
			Assert.ArgumentNotNull(target, "target");
			int result = 0;
			int num = 0;
			foreach (Item child in target.Parent.Children)
			{
				this.SetItemSortorder(child, num * 100);
				if (child.ID == target.ID)
				{
					result = ((dragAction == DragAction.Before) ? (num * 100 - 50) : (num * 100 + 50));
				}
				num++;
			}
			return result;
		}

		protected virtual void SetItemSortorder(Item item, int sortorder)
		{
			Assert.ArgumentNotNull(item, "item");
			item.Editing.BeginEdit();
			item.Appearance.Sortorder = sortorder;
			item.Editing.EndEdit();
		}

		protected virtual void SetSortorder(Item item, ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(item, "item");
			Assert.ArgumentNotNull(args, "args");
			if (args.Parameters["appendAsChild"] != "1")
			{
				Item item2 = this.GetDatabase(args).GetItem(args.Parameters["target"]);
				if (item2 != null)
				{
					int num = item2.Appearance.Sortorder;
					if (args.Parameters["sortAfter"] == "1")
					{
						Item nextSibling = item2.Axes.GetNextSibling();
						if (nextSibling == null)
						{
							num += 100;
						}
						else
						{
							int sortorder = nextSibling.Appearance.Sortorder;
							if (Math.Abs(sortorder - num) >= 2)
							{
								num += (sortorder - num) / 2;
							}
							else if (item2.Parent != null)
							{
								num = this.Resort(item2, DragAction.After);
							}
						}
					}
					else
					{
						Item previousSibling = item2.Axes.GetPreviousSibling();
						if (previousSibling == null)
						{
							num -= 100;
						}
						else
						{
							int sortorder2 = previousSibling.Appearance.Sortorder;
							if (Math.Abs(sortorder2 - num) >= 2)
							{
								num -= (num - sortorder2) / 2;
							}
							else if (item2.Parent != null)
							{
								num = this.Resort(item2, DragAction.Before);
							}
						}
					}
					this.SetItemSortorder(item, num);
				}
			}
		}

		internal void StartMoveProcess(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			if (!EventDisabler.IsActive)
			{
				Event.RaiseEvent("item:bucketing:dragInto", args, this);
			}
			Database database = this.GetDatabase(args);
			Item source = this.GetSource(args, database);
			Item target = this.GetTarget(args);
			using (new SecurityDisabler())
			{
				args.Parameters["searchRootId"] = source.ParentID.ToString();
				Log.Audit(this, "Drag item: {0} to {1}", AuditFormatter.FormatItem(source), AuditFormatter.FormatItem(target));
				this.OutputMessage("Moving Items", "Moving item: {0}", source.Paths.ContentPath);
				this.MoveItemIntoBucket(source, target);
			}
		}

		protected virtual void MoveItemIntoBucket(Item item, Item target)
		{
			BucketManager.MoveItemIntoBucket(item, target);
		}

		internal void EndMoveProcess(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			string text = args.Parameters["searchRootId"];
			if (!string.IsNullOrEmpty(text) && ID.IsID(text))
			{
				Context.ClientPage.Dispatch("item:refresh(id=" + text + ")");
				Context.ClientPage.Dispatch("item:refreshchildren(id=" + text + ")");
				this.RepairLinks(args);
				args.AbortPipeline();
				if (!EventDisabler.IsActive)
				{
					Event.RaiseEvent("item:bucketing:dragged", args, this);
				}
			}
		}

		internal void StartCopyProcess(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			if (!EventDisabler.IsActive)
			{
				Event.RaiseEvent("item:bucketing:dragInto", args, this);
			}
			Database database = this.GetDatabase(args);
			Item source = this.GetSource(args, database);
			Item target = this.GetTarget(args);
			using (new SecurityDisabler())
			{
				Log.Audit(this, "Copy item: {0} to {1}", AuditFormatter.FormatItem(source), AuditFormatter.FormatItem(target));
				this.OutputMessage("Copying Items", "Copying item: {0}", source.Paths.ContentPath);
				Item item = this.CopyItem(source, target, true);
				if (item != null)
				{
					args.Parameters["postaction"] = "item:load(id=" + item.ID + ")";
				}
			}
		}

		protected virtual Item CopyItem(Item contextItem, Item target, bool deep)
		{
			return BucketManager.CopyItem(contextItem, target, true);
		}

		internal void EndCopyProcess(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			string text = args.Parameters["postaction"];
			if (!string.IsNullOrEmpty(text))
			{
				Context.ClientPage.SendMessage(this, text);
				args.AbortPipeline();
				if (!EventDisabler.IsActive)
				{
					Event.RaiseEvent("item:bucketing:dragged", args, this);
				}
			}
		}

		internal void RepairLinks(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			if (!(args.Parameters["copy"] == "1"))
			{
				Database database = this.GetDatabase(args);
				Item source = this.GetSource(args, database);
				JobManager.Start(new JobOptions("LinkUpdater", "LinkUpdater", Context.Site.Name, new LinkUpdaterJob(source), "Update")
				{
					ContextUser = Context.User
				});
			}
		}

		protected virtual Item GetTarget(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			Item item = this.GetDatabase(args).GetItem(args.Parameters["target"]);
			Assert.IsNotNull(item, typeof(Item), "ID:{0}", args.Parameters["target"]);
			if (args.Parameters["appendAsChild"] != "1")
			{
				item = item.Parent;
				Assert.IsNotNull(item, typeof(Item), "ID:{0}.Parent", args.Parameters["target"]);
			}
			return item;
		}

		protected virtual Database GetDatabase(ClientPipelineArgs args)
		{
			Assert.ArgumentNotNull(args, "args");
			Database database = Factory.GetDatabase(args.Parameters["database"]);
			Error.Assert(database != null, "Database \"" + args.Parameters["database"] + "\" not found.");
			return database;
		}

		protected virtual Item GetSource(ClientPipelineArgs args, Database database)
		{
			Assert.ArgumentNotNull(args, "args");
			Assert.ArgumentNotNull(database, "database");
			Item item = database.GetItem(args.Parameters["id"]);
			Assert.IsNotNull(item, typeof(Item), "ID:{0}", args.Parameters["id"]);
			return item;
		}

		protected virtual void OutputMessage(string jobName, string message, params object[] parameters)
		{
			Assert.IsNotNullOrEmpty(message, "message");
			Job job = JobManager.GetJob(jobName);
			if (job != null)
			{
				JobHelper.OutputMessage(job, message, parameters);
			}
		}
	}

}