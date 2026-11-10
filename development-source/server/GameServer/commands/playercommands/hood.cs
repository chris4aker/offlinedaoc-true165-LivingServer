 /*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */

namespace DOL.GS.Commands
{
	[CmdAttribute("&hood", //command to handle
		ePrivLevel.Player, //minimum privelege level
		"Toggles the hood on and off when wearing a hooded cloak.", //command description
		"/hood", "/hood <companion name> - your hired companion's hood (remembered)")] //usage
	public class HoodCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			if (IsSpammingCommand(client.Player, "hood"))
				return;

			if (args.Length > 1)
			{
				ToggleCompanionHood(client, string.Join(" ", args, 1, args.Length - 1));
				return;
			}

			client.Player.IsCloakHoodUp = !client.Player.IsCloakHoodUp;
		}

		/// <summary>
		/// /hood name: a hired companion in your group puts its hood up or down, as /hood does for you, and keeps it that
		/// way when it comes back (owner 2026-11-10).
		/// </summary>
		private void ToggleCompanionHood(GameClient client, string name)
		{
			GamePlayer player = client.Player;
			GameBot bot = FindCompanion(player, name);
			if (bot == null)
			{
				DisplayMessage(client, $"You have no companion called {name.Trim()} in your group.");
				return;
			}
			bot.IsCloakHoodUp = !bot.IsCloakHoodUp;
			bot.BroadcastLivingEquipmentUpdate();
			CompanionOptions165.SetHoodUp(HiredCompanions165.HiredRecord(bot), bot.IsCloakHoodUp);
			DisplayMessage(client, bot.IsCloakHoodUp ? $"{bot.Name} puts up the hood of their cloak." : $"{bot.Name} lowers the hood of their cloak.");
		}

		/// <summary>Your hired companion in your group by name: the whole name first, else the only one it begins.</summary>
		public static GameBot FindCompanion(GamePlayer player, string name)
		{
			string wanted = (name ?? string.Empty).Trim();
			if (player?.Group == null || wanted.Length == 0)
				return null;
			var mine = new System.Collections.Generic.List<GameBot>();
			foreach (GameLiving member in player.Group.GetMembersInTheGroup())
				if (member is GameBot bot && HiredCompanions165.IsHired(bot) && bot.CompanionPlayer == player)
					mine.Add(bot);
			GameBot exact = mine.Find(b => string.Equals(b.Name, wanted, System.StringComparison.OrdinalIgnoreCase));
			if (exact != null)
				return exact;
			var starts = mine.FindAll(b => b.Name.StartsWith(wanted, System.StringComparison.OrdinalIgnoreCase));
			return starts.Count == 1 ? starts[0] : null;
		}
	}
}