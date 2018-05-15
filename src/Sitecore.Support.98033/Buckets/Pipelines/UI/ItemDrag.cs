using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
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

namespace Sitecore.Support.Buckets.Pipelines.UI
{
  public class ItemDrag : ItemOperation
  { 
      internal void EndCopyProcess(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        string str = args.Parameters["postaction"];
        if (!string.IsNullOrEmpty(str))
        {
          Context.ClientPage.SendMessage(this, str);
          args.AbortPipeline();
          if (!EventDisabler.IsActive)
          {
            Event.RaiseEvent("item:bucketing:dragged", new object[] { args, this });
          }
        }
      }

      internal void EndMoveProcess(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        string str = args.Parameters["searchRootId"];
        if (!string.IsNullOrEmpty(str) && ID.IsID(str))
        {
          Context.ClientPage.Dispatch("item:refresh(id=" + str + ")");
          Context.ClientPage.Dispatch("item:refreshchildren(id=" + str + ")");
          this.RepairLinks(args);
          args.AbortPipeline();
          if (!EventDisabler.IsActive)
          {
            Event.RaiseEvent("item:bucketing:dragged", new object[] { args, this });
          }
        }
    }

      public void Execute(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        Database database = GetDatabase(args);
        Item source = GetSource(args, database);
        if ((BucketManager.IsBucket(GetTarget(args)) || BucketManager.IsBucket(source)) || BucketManager.IsItemContainedWithinBucket(source))
        {
          if (args.Parameters["copy"] == "1")
          {
            ProgressBox.ExecuteSync("Copying Items", "~/icon/Core3/32x32/copy_to_folder.png", new Action<ClientPipelineArgs>(this.StartCopyProcess), new Action<ClientPipelineArgs>(this.EndCopyProcess));
          }
          else
          {
            ProgressBox.ExecuteSync("Moving Items", "~/icon/Core3/32x32/move_to_folder.png", new Action<ClientPipelineArgs>(this.StartMoveProcess), new Action<ClientPipelineArgs>(this.EndMoveProcess));
          }
        }
      }

      private static Database GetDatabase(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        Database database = Factory.GetDatabase(args.Parameters["database"]);
        Error.Assert(database != null, "Database \"" + args.Parameters["database"] + "\" not found.");
        return database;
      }

      private static Item GetSource(ClientPipelineArgs args, Database database)
      {
        Assert.ArgumentNotNull(args, "args");
        Assert.ArgumentNotNull(database, "database");
        Item item = database.GetItem(args.Parameters["id"]);
        Assert.IsNotNull(item, typeof(Item), "ID:{0}", new object[] { args.Parameters["id"] });
        return item;
      }

      private static Item GetTarget(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        Item parent = GetDatabase(args).GetItem(args.Parameters["target"]);
        Assert.IsNotNull(parent, typeof(Item), "ID:{0}", new object[] { args.Parameters["target"] });
        if (args.Parameters["appendAsChild"] != "1")
        {
          parent = parent.Parent;
          Assert.IsNotNull(parent, typeof(Item), "ID:{0}.Parent", new object[] { args.Parameters["target"] });
        }
        return parent;
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

      internal void RepairLinks(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        if (args.Parameters["copy"] != "1")
        {
          Database database = GetDatabase(args);
          Item source = GetSource(args, database);
          JobOptions options = new JobOptions("LinkUpdater", "LinkUpdater", Context.Site.Name, new LinkUpdaterJob(source), "Update")
          {
            ContextUser = Context.User
          };
          JobManager.Start(options);
        }
      }

      internal static int Resort(Item target, DragAction dragAction)
      {
        Assert.ArgumentNotNull(target, "target");
        int num = 0;
        int num2 = 0;
        foreach (Item item in target.Parent.Children)
        {
          item.Editing.BeginEdit();
          item.Appearance.Sortorder = num2 * 100;
          item.Editing.EndEdit();
          if (item.ID == target.ID)
          {
            num = (dragAction == DragAction.Before) ? ((num2 * 100) - 50) : ((num2 * 100) + 50);
          }
          num2++;
        }
        return num;
      }

      internal static void SetItemSortorder(Item item, int sortorder)
      {
        Assert.ArgumentNotNull(item, "item");
        item.Editing.BeginEdit();
        item.Appearance.Sortorder = sortorder;
        item.Editing.EndEdit();
      }

      internal static void SetSortorder(Item item, ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(item, "item");
        Assert.ArgumentNotNull(args, "args");
        if (args.Parameters["appendAsChild"] != "1")
        {
          Item target = GetDatabase(args).GetItem(args.Parameters["target"]);
          if (target != null)
          {
            int sortorder = target.Appearance.Sortorder;
            if (args.Parameters["sortAfter"] == "1")
            {
              Item nextSibling = target.Axes.GetNextSibling();
              if (nextSibling == null)
              {
                sortorder += 100;
              }
              else
              {
                int num2 = nextSibling.Appearance.Sortorder;
                if (Math.Abs((int)(num2 - sortorder)) >= 2)
                {
                  sortorder += (num2 - sortorder) / 2;
                }
                else if (target.Parent != null)
                {
                  sortorder = Resort(target, DragAction.After);
                }
              }
            }
            else
            {
              Item previousSibling = target.Axes.GetPreviousSibling();
              if (previousSibling == null)
              {
                sortorder -= 100;
              }
              else
              {
                int num3 = previousSibling.Appearance.Sortorder;
                if (Math.Abs((int)(num3 - sortorder)) >= 2)
                {
                  sortorder -= (sortorder - num3) / 2;
                }
                else if (target.Parent != null)
                {
                  sortorder = Resort(target, DragAction.Before);
                }
              }
            }
            SetItemSortorder(item, sortorder);
          }
        }
      }

      internal void StartCopyProcess(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        if (!EventDisabler.IsActive)
        {
          Event.RaiseEvent("item:bucketing:dragInto", new object[] { args, this });
        }
        Database database = GetDatabase(args);
        Item source = GetSource(args, database);
        Item target = GetTarget(args);
        using (new SecurityDisabler())
        {
          Log.Audit(this, "Copy item: {0} to {1}", new string[] { AuditFormatter.FormatItem(source), AuditFormatter.FormatItem(target) });
          this.OutputMessage("Copying Items", "Copying item: {0}", new object[] { source.Paths.ContentPath });
          Item item3 = BucketManager.CopyItem(source, target, true);
          if (item3 != null)
          {
            args.Parameters["postaction"] = "item:load(id=" + item3.ID + ")";
          }
        }
      }

      internal void StartMoveProcess(ClientPipelineArgs args)
      {
        Assert.ArgumentNotNull(args, "args");
        if (!EventDisabler.IsActive)
        {
          Event.RaiseEvent("item:bucketing:dragInto", new object[] { args, this });
        }
        Database database = GetDatabase(args);
        Item source = GetSource(args, database);
        Item target = GetTarget(args);
        using (new SecurityDisabler())
        {
          args.Parameters["searchRootId"] = source.ParentID.ToString();
          Log.Audit(this, "Drag item: {0} to {1}", new string[] { AuditFormatter.FormatItem(source), AuditFormatter.FormatItem(target) });
          this.OutputMessage("Moving Items", "Moving item: {0}", new object[] { source.Paths.ContentPath });
          BucketManager.MoveItemIntoBucket(source, target);
        }
      }
    }
  }
