
  const filterButtons = document.querySelectorAll(".filter-btn");
  const courseCards = document.querySelectorAll(".course-card");

  filterButtons.forEach(button => {
    // Set initial background classes
    const bg = button.dataset.bg.split(" ");
    button.classList.add(...bg);

    button.addEventListener("click", () => {
      const filter = button.dataset.filter;

      // Show/hide courses
      courseCards.forEach(card => {
        card.style.display = (filter === "all" || card.dataset.level === filter) ? "block" : "none";
      });

      // Reset all buttons to original background
      filterButtons.forEach(btn => {
        btn.classList.remove("ring-4", "ring-white", "scale-105");

        // Remove any bg-* or hover:bg-* classes
        btn.classList.forEach(cls => {
          if (cls.startsWith("bg-") || cls.startsWith("hover:bg-")) {
            btn.classList.remove(cls);
          }
        });

        // Re-apply original background
        const originalBg = btn.dataset.bg.split(" ");
        btn.classList.add(...originalBg);
      });

      // Highlight the active button
      button.classList.add("ring-4", "ring-white", "scale-105");
    });
  });
