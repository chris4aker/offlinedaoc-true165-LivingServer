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
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS.Commands
{
	[CmdAttribute(
		"&autoloot",
		ePrivLevel.Player,
		"automaticly pick up any loot that drops in your area",
		"/autoloot <on/off>")]
	public class AutolootCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			if (args.Length < 2)
			{
				DisplaySyntax(client);
				return;
			}

			// "quiet": the new client's settings send it at every login and toggle, without a chat line (True 1.65, owner
			// 2026-11-05); in True 1.65 Mode the choice is the session's (Autoloot165), off until the client says on
			bool quiet = args.Length > 2 && args[2].ToLower().Equals("quiet");
			if (args[1].ToLower().Equals("on"))
			{
				if (Classic165Mode.IsOn)
					Autoloot165.Set(client.Player, true);
				else
					client.Player.Autoloot = true;
				if (!quiet)
					DisplayMessage(client, LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Autoloot.On"));
            }
			else if (args[1].ToLower().Equals("off"))
			{
				if (Classic165Mode.IsOn)
					Autoloot165.Set(client.Player, false);
				else
					client.Player.Autoloot = false;
				if (!quiet)
					DisplayMessage(client, LanguageMgr.GetTranslation(client.Account.Language, "Scripts.Players.Autoloot.Off"));
            }
		}
	}
}