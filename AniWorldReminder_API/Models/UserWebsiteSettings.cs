﻿namespace AniWorldReminder_API.Models
{
    public class UserWebsiteSettings
    {
        public int Id {  get; set; }
        public int TelegramDisableNotifications { get; set; }
        public int TelegramNoCoverArtNotifications { get; set; }
    }
}
