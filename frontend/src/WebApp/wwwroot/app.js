window.scrollElementToBottom = (element) => {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};
