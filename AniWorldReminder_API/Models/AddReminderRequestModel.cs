﻿namespace AniWorldReminder_API.Models
{
    public class AddReminderRequestModel
    {
        public string? SeriesPath { get; set; }
        public StreamingPortal StreamingPortal { get; set; }
        public Language Language { get; set; }
    }
}
