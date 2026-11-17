// Copyright (C) 2024  Roland Breitschaft

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace Jellyfin.Xtream.Library;

/// <summary>
/// How a template value is escaped when it is inserted with <c>{{Name}}</c>.
/// </summary>
public enum WebhookEscape
{
    /// <summary>Inserted as it is.</summary>
    None,

    /// <summary>Escaped to sit inside a JSON string.</summary>
    Json,

    /// <summary>Percent-encoded, for a form-urlencoded body.</summary>
    Url,
}
