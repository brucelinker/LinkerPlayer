using LinkerPlayer.Windows;
using Serilog;
using System;
using System.Data.SQLite;
using System.IO;

namespace LinkerPlayer.Core;

// ReSharper disable once InconsistentNaming
public class SQLiteConnector
{
    private SQLiteConnection? _sqlConnection;

    public void Init()
    {
        string DatabaseFile = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LinkerPlayer", "LinkerPlayer.db");


        if (!File.Exists(DatabaseFile))
        {
            SQLiteConnection.CreateFile(DatabaseFile);
        }

        _sqlConnection = new SQLiteConnection("Data Source='" + DatabaseFile + "'; Version=3;");
        _sqlConnection.Open();

        Create();

        GetTables();
    }

    void Create()
    {
        string sql = @"CREATE TABLE IF NOT EXISTS 'Tracks' (
            'id' INTEGER PRIMARY KEY AUTOINCREMENT UNIQUE,
            'Path' TEXT,
            'FileName' TEXT,
            'Track' TEXT,
            'TrackCount' INTEGER,
            'Disc' INTEGER,
            'DiscCount' INTEGER,
            'Year' INTEGER,
            'Title' TEXT,
            'Album' TEXT,
            'Artists' TEXT,
            'Performers' TEXT,
            'Composers' TEXT,
            'Genres' TEXT,
            'Comment' TEXT,
            'Duration' INTEGER,
            'Bitrate' INTEGER,
            'SampleRate' INTEGER,
            'Channels' INTEGER,
            'Copyright' TEXT,
            'AlbumCover' TEXT
            );";
        SQLiteCommand command = new(sql, _sqlConnection);
        command.ExecuteNonQuery();

        sql = @"CREATE TABLE IF NOT EXISTS `Playlists` (
	        'Id' INTEGER PRIMARY KEY AUTOINCREMENT UNIQUE,
	        'Name' TEXT NOT NULL,
	        'SongId' INTEGER NOT NULL
        );";
        command = new SQLiteCommand(sql, _sqlConnection);
        command.ExecuteNonQuery();

        sql = @"CREATE TABLE IF NOT EXISTS `Presets` (
	        'Id' INTEGER PRIMARY KEY AUTOINCREMENT UNIQUE,
	        'Name' TEXT NOT NULL,
            'Locked' BOOLEAN NOT NULL
	        'Band0' FLOAT(3,1) NOT NULL,
	        'Band1' FLOAT(3,1) NOT NULL,
	        'Band2' FLOAT(3,1) NOT NULL,
	        'Band3' FLOAT(3,1) NOT NULL,
	        'Band4' FLOAT(3,1) NOT NULL,
	        'Band5' FLOAT(3,1) NOT NULL,
	        'Band6' FLOAT(3,1) NOT NULL,
	        'Band7' FLOAT(3,1) NOT NULL,
	        'Band8' FLOAT(3,1) NOT NULL,
	        'Band9' FLOAT(3,1) NOT NULL,
        );";
        command = new SQLiteCommand(sql, _sqlConnection);
        command.ExecuteNonQuery();

    }

    //private const string SqlGetTrack = "SELECT COUNT(id) as count, id FROM RecentTracks WHERE path = '{0}'";
    //private const string SqlInsertTrack = "INSERT INTO RecentTracks (path, playcount, playdate) VALUES ('{0}',1,'{1}')";
    //private const string SqlUpdateTrack = "UPDATE RecentTracks SET playdate = '{1}', playcount = playcount + 1 WHERE id = {0}";

    //private const string SqlGetAlbum = "SELECT COUNT(id) as count, id FROM RecentAlbums where name = '{0}' and year = '{1}'";
    //private const string SqlInsertAlbum = "INSERT INTO RecentAlbums (name, year, playcount, playdate) VALUES ('{0}', '{1}', 1, '{2}')";
    //private const string SqlUpdateAlbum = "Update RecentAlbums SET playdate = '{1}', playcount = playcount + 1 WHERE id = {0}";

    //private const string SqlInsertAlbumTrack = "INSERT INTO AlbumTracks (albumId, path) VALUES ({0}, '{1}')";
    //private const string SqlGetAlbumTrack = "SELECT id FROM AlbumTracks WHERE albumId = '{0}' AND path = '{1}' LIMIT 1";
    //private const string SqlGetAlbumTrackCount = "SELECT COUNT(id) as count FROM AlbumTracks WHERE Albumid = '{0}'";

    private const string SqlGetTables = "SELECT name FROM sqlite_master WHERE type = 'table'";

    //private const string SqlGetRecentAlbums = "SELECT * FROM (SELECT id, name, year, playcount FROM  RecentAlbums WHERE name <> '' ORDER BY playdate DESC LIMIT 400) ORDER BY playcount DESC LIMIT {0}";
    //private const string SqlGetRecentTracks = "SELECT * FROM (SELECT path, playcount from RecentTracks ORDER BY playdate DESC LIMIT 10000) ORDER BY playcount DESC LIMIT {0}";
    //private const string SqlGetRecentTracksDetail = "SELECT * FROM (SELECT path, playcount, playdate from RecentTracks ORDER BY playdate DESC LIMIT 10000) ORDER BY playcount DESC LIMIT {0}";
    //private const string SqlGetAlbumTracks = "SELECT path from AlbumTracks WHERE albumId = '{0}'";

    //private const string SqlRemoveAlbum = "DELETE FROM RecentALbums WHERE id = '{0}'";
    //private const string SqlRemoveAlbumTracks = "DELETE FROM AlbumTracks WHERE albumId = '{0}'";

    //private static string get_sql_date(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss");

    //private static string Sanitize(string str) => str.Replace("'", "{!x%99}");

    //private static string Desanitize(string str) => str.Replace("{!x%99}", "'");

    private void GetTables()
    {
        string sql = string.Format(SqlGetTables);
        SQLiteCommand command = new(sql, _sqlConnection);

        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string str = reader.ToString() ?? string.Empty;
            Log.Information(str);
        }
    }

    //private int GetAlbum(string name, string year)
    //{
    //    string sql = String.Format(SqlGetAlbum, name, year);
    //    SQLiteCommand command = new(sql, _sqlConnection);

    //    using SQLiteDataReader reader = command.ExecuteReader();
    //    if (reader.StepCount < 1 || !reader.HasRows)
    //        return -1;

    //    reader.Read();
    //    int.TryParse(reader["count"].ToString(), out int count);

    //    if (count == 0)
    //        return -1;

    //    int.TryParse(reader["id"].ToString(), out int id);
    //    return id;
    //}

    //public int GetAlbumTrackCount(int id)
    //{
    //    string sql = String.Format(SqlGetAlbumTrackCount, id);
    //    SQLiteCommand command = new(sql, _sqlConnection);

    //    using SQLiteDataReader reader = command.ExecuteReader();
    //    if (int.TryParse(reader["count"].ToString(), out int count))
    //        return count;
    //    else
    //        return 0;
    //}

    //private int GetTrack(string path)
    //{
    //    string sql = String.Format(SqlGetTrack, Sanitize(path));
    //    SQLiteCommand command = new(sql, _sqlConnection);

    //    using SQLiteDataReader reader = command.ExecuteReader();
    //    reader.Read();

    //    if (reader.StepCount < 1 || !reader.HasRows)
    //        return -1;

    //    int.TryParse(reader["count"].ToString(), out int count);

    //    if (count == 0)
    //        return -1;
    //    else
    //    {
    //        int.TryParse(reader["id"].ToString(), out int id);
    //        return id;
    //    }
    //}

    //private string GetDate() => get_sql_date(DateTime.Now);

    //private void InsertTrack(string path)
    //{
    //    string sql = String.Format(SqlInsertTrack, Sanitize(path), GetDate());
    //    SQLiteCommand command = new(sql, _sqlConnection);
    //    command.ExecuteNonQuery();
    //}

    //private void InsertAlbum(string name, string year)
    //{
    //    string sql = String.Format(SqlInsertAlbum, name, year, GetDate());
    //    SQLiteCommand command = new(sql, _sqlConnection);
    //    command.ExecuteNonQuery();
    //}

    //private void UpdateTrack(int id)
    //{
    //    string sql = String.Format(SqlUpdateTrack, id.ToString(), GetDate());
    //    SQLiteCommand command = new(sql, _sqlConnection);
    //    command.ExecuteNonQuery();
    //}

    //private void UpdateAlbum(int id)
    //{
    //    string sql = String.Format(SqlUpdateAlbum, id.ToString(), GetDate());
    //    SQLiteCommand command = new(sql, _sqlConnection);
    //    command.ExecuteNonQuery();
    //}

    //private bool GetAlbumTrack(int albumId, string track)
    //{
    //    string sql = String.Format(SqlGetAlbumTrack, albumId, Sanitize(track));
    //    SQLiteCommand command = new(sql, _sqlConnection);

    //    SQLiteDataReader reader = command.ExecuteReader();
    //    reader.Read();

    //    if (reader.StepCount == 0 || !reader.HasRows)
    //        return false;

    //    int.TryParse(reader["id"].ToString(), out int id);
    //    return id >= 0;
    //}

    //private void InsertAlbumTrack(int id, string track)
    //{
    //    string sql = String.Format(SqlInsertAlbumTrack, id.ToString(), Sanitize(track));
    //    SQLiteCommand command = new(sql, _sqlConnection);
    //    command.ExecuteNonQuery();
    //}

    //public void SetTrack(string path)
    //{
    //    int id = GetTrack(path);
    //    if (id < 0)
    //        InsertTrack(path);
    //    else
    //        UpdateTrack(id);
    //}

    //public List<Tuple<string, int, DateTime>> GetRecentTracksDetails(int num)
    //{
    //    string sql = String.Format(SqlGetRecentTracksDetail, num);
    //    List<Tuple<string, int, DateTime>> tracks = new();

    //    SQLiteCommand command = new(sql, _sqlConnection);
    //    using SQLiteDataReader reader = command.ExecuteReader();
    //    while (reader.Read())
    //    {

    //        if (reader.StepCount < 1 || !reader.HasRows)
    //            return tracks;

    //        string path = Desanitize(reader["path"].ToString()!);
    //        if (!int.TryParse(reader["playCount"].ToString(), out int playCount))
    //            playCount = 1;
    //        string strDate = reader["playDate"].ToString()!;

    //        DateTime myDate = DateTime.Parse(strDate, System.Globalization.CultureInfo.InvariantCulture);

    //        Tuple<string, int, DateTime> t = new(path, playCount, myDate);

    //        tracks.Add(t);
    //    }

    //    return tracks;
    //}

    //public List<string> GetRecentTracks(int num)
    //{
    //    string sql = String.Format(SqlGetRecentTracks, num);
    //    List<string> tracks = new();

    //    SQLiteCommand command = new(sql, _sqlConnection);

    //    using SQLiteDataReader reader = command.ExecuteReader();
    //    while (reader.Read())
    //    {

    //        if (reader.StepCount < 1 || !reader.HasRows)
    //            return tracks;

    //        tracks.Add(Desanitize(reader["path"].ToString()!));
    //    }

    //    return tracks;
    //}

    //public List<Tuple<string, string, int>> GetRecentAlbums(int num)
    //{

    //    string sql = String.Format(SqlGetRecentAlbums, num);
    //    List<Tuple<string, string, int>> albums = new();
    //    SQLiteCommand command = new(sql, _sqlConnection);

    //    using SQLiteDataReader reader = command.ExecuteReader();
    //    while (reader.Read())
    //    {
    //        if (reader.StepCount < 1 || !reader.HasRows)
    //            return albums;

    //        if (!int.TryParse(reader["id"].ToString(), out int id) || reader["name"].ToString() == "")
    //            continue;

    //        albums.Add(new Tuple<string, string, int>(reader["name"].ToString()!, reader["year"].ToString()!, id));

    //    }

    //    return albums;
    //}

    //public string[] GetAlbumTracks(int id)
    //{
    //    List<string> t = new();
    //    string sql = String.Format(SqlGetAlbumTracks, id);

    //    SQLiteCommand command = new(sql, _sqlConnection);

    //    using (SQLiteDataReader reader = command.ExecuteReader())
    //    {
    //        while (reader.Read())
    //        {
    //            if (reader.StepCount < 1 || !reader.HasRows)
    //                return t.ToArray();

    //            t.Add(Desanitize(reader["path"].ToString()!));
    //        }
    //    }

    //    return t.Distinct().ToArray();
    //}


    //public void SetAlbum(string name, string year, string[] tracks)
    //{
    //    string albumName = Sanitize(name);

    //    int id = GetAlbum(albumName, year);
    //    if (id < 0)
    //    {
    //        InsertAlbum(albumName, year);
    //        id = GetAlbum(albumName, year);
    //        if (id < 0)
    //            return;
    //    }
    //    else
    //    {
    //        UpdateAlbum(id);
    //    }

    //    foreach (string track in tracks)
    //    {
    //        string t = Sanitize(track);
    //        if (!GetAlbumTrack(id, t))
    //            InsertAlbumTrack(id, t);
    //    }
    //}

    //public void RemoveAlbum(int id)
    //{
    //    string sqlAlbums = string.Format(SqlRemoveAlbum, id);
    //    string sqlTracks = string.Format(SqlRemoveAlbumTracks, id);
    //    SQLiteCommand command = new(sqlAlbums, _sqlConnection);
    //    command.ExecuteNonQuery();
    //    command = new SQLiteCommand(sqlTracks, _sqlConnection);
    //    command.ExecuteNonQuery();
    //}
}
