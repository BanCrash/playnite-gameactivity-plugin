﻿using System.Windows;
using System.Windows.Controls.Primitives;

namespace GameActivity.Views.Interface
{
    /// <summary>
    /// Logique d'interaction pour GameActivityToggle.xaml
    /// </summary>
    public partial class GameActivityToggleButton : ToggleButton
    {
        public GameActivityToggleButton(GameActivitySettings settings)
        {
            InitializeComponent();

            if (settings.EnableIntegrationInDescriptionOnlyIcon)
            {
                PART_ButtonIcon.Visibility = Visibility.Visible;
                PART_ButtonText.Visibility = Visibility.Collapsed;
            }
            else
            {
                PART_ButtonIcon.Visibility = Visibility.Collapsed;
                PART_ButtonText.Visibility = Visibility.Visible;
            }
        }
    }
}
