using System;
using System.Collections;
using System.Windows;
using OverWatchELD.Services;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class CleanCreateLoadWindow : Window
    {
        private readonly object? _vm;

        public DispatchJob? SavedJob { get; internal set; }

        public CleanCreateLoadWindow()
        {
            InitializeComponent();

            // IMPORTANT:
            // The XAML is fully bound to CleanCreateLoadViewModel,
            // but the XAML does not create/set DataContext.
            // Without this, all dropdowns stay empty.
            DataContext = new CleanCreateLoadViewModel();
            _vm = DataContext;
        }

        public CleanCreateLoadWindow(DispatchJob job) : this()
        {
            if (_vm == null || job == null)
                return;

            SavedJob = job;

            Loaded += (_, _) =>
            {
                try
                {
                    SetVmValue("LoadNumber", job.LoadNumber);

                    SetVmValue("WeightLbs", (int)Math.Round(job.CargoWeight > 0
                        ? job.CargoWeight
                        : job.ActualCargoWeightLbs));

                    SetVmValue("Miles", job.Miles);

                    SetSelectedByName(
                        "CargoOptions",
                        "SelectedCargo",
                        "Name",
                        job.Cargo);

                    SetSelectedByName(
                        "TrailerOptions",
                        "SelectedTrailer",
                        "Name",
                        job.Trailer);

                    SetSelectedByName(
                        "DriverOptions",
                        "SelectedDriver",
                        "DisplayName",
                        job.AssignedDriver);

                    SetSelectedByName(
                        "TruckOptions",
                        "SelectedTruck",
                        "DisplayName",
                        job.AssignedTruck);

                    SetSelectedContains(
                        "PickupCompaniesView",
                        "SelectedPickupCompany",
                        "DisplayName",
                        job.Company);

                    SetSelectedContains(
                        "DestinationCompaniesView",
                        "SelectedDestinationCompany",
                        "DisplayName",
                        job.Company);
                }
                catch
                {
                }
            };
        }

        private void SetVmValue(string propertyName, object? value)
        {
            try
            {
                var prop = _vm?.GetType().GetProperty(propertyName);

                if (prop != null && prop.CanWrite)
                    prop.SetValue(_vm, value);
            }
            catch
            {
            }
        }

        private void SetSelectedByName(
            string listProperty,
            string selectedProperty,
            string compareProperty,
            string? targetValue)
        {
            if (string.IsNullOrWhiteSpace(targetValue))
                return;

            try
            {
                var listProp = _vm?.GetType().GetProperty(listProperty);
                var selectedProp = _vm?.GetType().GetProperty(selectedProperty);

                if (listProp == null || selectedProp == null)
                    return;

                var items = listProp.GetValue(_vm) as IEnumerable;

                if (items == null)
                    return;

                foreach (var item in items)
                {
                    var value = item?
                        .GetType()
                        .GetProperty(compareProperty)?
                        .GetValue(item)?
                        .ToString();

                    if (!string.IsNullOrWhiteSpace(value) &&
                        value.Trim().Equals(
                            targetValue.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        selectedProp.SetValue(_vm, item);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private void SetSelectedContains(
            string listProperty,
            string selectedProperty,
            string compareProperty,
            string? targetValue)
        {
            if (string.IsNullOrWhiteSpace(targetValue))
                return;

            try
            {
                var listProp = _vm?.GetType().GetProperty(listProperty);
                var selectedProp = _vm?.GetType().GetProperty(selectedProperty);

                if (listProp == null || selectedProp == null)
                    return;

                var items = listProp.GetValue(_vm) as IEnumerable;

                if (items == null)
                    return;

                foreach (var item in items)
                {
                    var value = item?
                        .GetType()
                        .GetProperty(compareProperty)?
                        .GetValue(item)?
                        .ToString();

                    if (!string.IsNullOrWhiteSpace(value) &&
                        value.Contains(
                            targetValue,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        selectedProp.SetValue(_vm, item);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private void OpenCompanyLoads_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new CompanyLoadBoardWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Company Loads window.\n\n" + ex.Message,
                    "Company Loads",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void OpenLoadHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new LoadHistoryWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Load History window.\n\n" + ex.Message,
                    "Load History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }
    }
}