﻿using Shiny.SpeechRecognition;

namespace BeatTheBank;


public class MainViewModel : ViewModel
{
    readonly ITextToSpeech textToSpeech;
    readonly ISpeechRecognizer speechRecognizer;
    readonly SoundEffectService sounds;
    readonly Random randomizer = new();
    int rounds = 0;
    bool isJackpot = false;


    public MainViewModel(
        BaseServices services,
        ITextToSpeech textToSpeech,
        ISpeechRecognizer speechRecognizer,
        SoundEffectService sounds
    ) : base(services)
    {
        this.textToSpeech = textToSpeech;
        this.speechRecognizer = speechRecognizer;
        this.sounds = sounds;

        this.StartOver = ReactiveCommand.CreateFromTask
        (
            async () =>
            {
                this.Vault = 0;
                this.Amount = 0;
                this.WinAmount = 0;
                this.StopVault = 0;
                this.rounds = this.randomizer.Next(4, 15);
                this.isJackpot = this.randomizer.Next(1, 40) == 39; // 1 in 40 chance

                Console.WriteLine($"Rounds: {this.rounds} - Jackpot: {this.isJackpot}");

                await this.Speak(1000, $"Good Luck {this.Name}.  Let's play!");
                await this.NextRound();
            },
            this.WhenAny(
                x => x.Name,
                x => !x.GetValue().IsEmpty()
            )
        );

        this.Continue = ReactiveCommand.CreateFromTask(
            async () => await this.NextRound(),
            this.WhenAny(
                x => x.Vault,
                x => x.Status,
                (v, st) => v.GetValue() < this.rounds && st.GetValue() == PlayState.InProgress
            )
        );

        this.Stop = ReactiveCommand.CreateFromTask
        (
            async () =>
            {
                this.Status = PlayState.WinStop;
                this.WinAmount = this.Amount;
                this.StopVault = this.Vault;

                await this.Speak(
                    500,
                    $"Good Job {this.Name}",
                    $"You won {this.Amount} dollars",
                    "Let's see what you could have won"
                );

                while (await this.TryNextRound())
                    await Task.Delay(500);
            },
            this.WhenAny(
                x => x.Vault,
                x => x.Status,
                (v, st) => v.GetValue() > 0 && st.GetValue() == PlayState.InProgress
            )
        );

        this.Speech = ReactiveCommand.CreateFromTask(async () =>
        {
            var result = await this.speechRecognizer.RequestAccess();
            if (result == AccessState.Available)
            {
                this.speechRecognizer
                    .ListenUntilPause()
                    .Where(x =>
                    {
                        if (!this.Continue.CanExecute(null))
                            return false;

                        var v = x.ToLower();
                        if (v.Contains("continue"))
                            return true;

                        return false;
                    })
                    .SubOnMainThread(_ => this.Continue!.Execute(null));
            }
        });
    }


    [Reactive] public string Name { get; set; }
    [Reactive] public int Vault { get; private set; }
    [Reactive] public int StopVault { get; private set; }
    [Reactive] public int WinAmount { get; private set; }
    [Reactive] public int Amount { get; private set; }
    [Reactive] public PlayState Status { get; private set; }
    public ICommand StartOver { get; }
    public ICommand Continue { get; }
    public ICommand Speech { get; }
    public ICommand Stop { get; }


    public override void OnNavigatedTo(INavigationParameters parameters)
    {
        base.OnNavigatedTo(parameters);
        //this.Speech.Execute(null);
    }


    async Task NextRound()
    {
        await this.Speak(1000, "Alright, let's open it up.");
        if (await this.TryNextRound())
            await this.textToSpeech.SpeakAsync("Do you wish to continue?");
    }


    async Task<bool> TryNextRound()
    {
        var next = false;
        this.Vault++;

        if (this.Vault == this.rounds)
        {
            if (this.isJackpot)
            {
                if (this.Status != PlayState.WinStop)
                {
                    this.Status = PlayState.Win;
                    this.WinAmount = 1000000;
                }
                this.sounds.PlayJackpot();
            }
            else
            {
                if (this.Status != PlayState.WinStop)
                {
                    this.WinAmount = 0;
                    this.Status = PlayState.Lose;
                }
                this.sounds.PlayAlarm();
            }
        }
        else
        {
            if (this.Status != PlayState.WinStop)
                this.Status = PlayState.InProgress;

            this.Amount += this.GetNextAmount();
            await this.Speak(
                500,
                $"Vault {this.Vault}",
                $"{this.Amount} dollars"
            );
            next = true;
        }
        return next;
    }


    List<int> amounts;
    int GetNextAmount()
    {
        if (this.amounts == null)
        {
            this.amounts = new();
            this.amounts.AddRange(Enumerable.Repeat(50, 10));
            this.amounts.AddRange(Enumerable.Repeat(100, 25));
            this.amounts.AddRange(Enumerable.Repeat(200, 25));
            this.amounts.AddRange(Enumerable.Repeat(250, 25));
            this.amounts.AddRange(Enumerable.Repeat(300, 25));
            this.amounts.AddRange(Enumerable.Repeat(500, 25));
            this.amounts.AddRange(Enumerable.Repeat(1000, 25));
        }
        var index = new Random().Next(0, this.amounts.Count);
        var amount = this.amounts[index];
        return amount;
    }


    async Task Speak(int pauseBetween, params string[] sentences)
    {
        foreach (var s in sentences)
        {
            await this.textToSpeech.SpeakAsync(s);
            await Task.Delay(pauseBetween);
        }
    }
}


public enum PlayState
{
    InProgress,
    Win,
    WinStop,
    Lose
}