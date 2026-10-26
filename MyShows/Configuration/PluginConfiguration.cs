using System;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace MyShows.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public UserConfig[] Users { get; set; }

        public PluginConfiguration()
        {
            Users = Array.Empty<UserConfig>();
        }

        public UserConfig GetUserById(Guid id)
        {
            var gid = id.ToString().Replace("-", "");
            return Users.FirstOrDefault(c => c.Id == gid);
        }

        public void AddUser(UserConfig user)
        {
            var users = Users.ToList();
            var index = users.FindIndex(u => u.Id == user.Id);
            if (index == -1)
            {
                users.Add(user);
            }
            else
            {
                user.ScrobbleAt = users[index].ScrobbleAt;
                user.PullWatchedFromMyShows = users[index].PullWatchedFromMyShows;
                users[index] = user;
            }
            Users = users.ToArray();
            Plugin.Instance.SaveConfiguration();
        }
    }
}
