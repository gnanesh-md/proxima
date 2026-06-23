
    const phrases = [
      "impactful internships?",
      "job-ready skills?",
      "real-world exposure?",
      "certified programs?",
      "live project experience?",
      "100% placement support?",
      "industry-relevant content?",
      "future-proof careers?"
    ];

    let phraseIndex = 0;
    let charIndex = 0;
    let isDeleting = false;

    function typeEffect() {
      const el = document.getElementById("typewriter");
      const currentPhrase = phrases[phraseIndex];

      if (!isDeleting && charIndex <= currentPhrase.length) {
        el.textContent = currentPhrase.substring(0, charIndex++);
      } else if (isDeleting && charIndex >= 0) {
        el.textContent = currentPhrase.substring(0, charIndex--);
      }

      if (charIndex === currentPhrase.length + 1) {
        isDeleting = true;
        setTimeout(typeEffect, 1200);
        return;
      }

      if (isDeleting && charIndex === 0) {
        isDeleting = false;
        phraseIndex = (phraseIndex + 1) % phrases.length;
      }

      setTimeout(typeEffect, isDeleting ? 40 : 90);
    }

    window.onload = typeEffect;
